// Patcher.cs
// ============================================================
// 用 Mono.Cecil 自动 patch EcoChemie100.dll
// 在 ExternalRS232.Send/Receive 中插入对 ModbusHelper 的调用
// ============================================================
// 2025-08-06 更新：
//   - 修复 PatchReceive fallback：从 return "" 改为调用 ReadLine()
// ============================================================

using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Patcher
{
    static int Main(string[] args)
    {
        string targetDir;
        if (args.Length > 0)
            targetDir = args[0];
        else if (File.Exists(Path.Combine(Environment.CurrentDirectory, "EcoChemie100.dll")))
            targetDir = Environment.CurrentDirectory;
        else
            targetDir = @"C:\Program Files\Metrohm Autolab\Nova 2.1";

        string dllPath = Path.Combine(targetDir, "EcoChemie100.dll");
        string helperDllPath = Path.Combine(Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location),
            "MyModbusExtension.dll");

        if (!File.Exists(dllPath))
        {
            Console.WriteLine("[错误] 找不到: " + dllPath);
            return 1;
        }
        if (!File.Exists(helperDllPath))
        {
            Console.WriteLine("[错误] 找不到: " + helperDllPath);
            return 1;
        }

        // 备份
        string bakPath = dllPath + ".bak";
        if (!File.Exists(bakPath))
        {
            File.Copy(dllPath, bakPath, true);
            Console.WriteLine("[备份] 已创建: " + bakPath);
        }

        // 解析器：让 Mono.Cecil 能解析 EcoChemie100 的依赖
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(targetDir);
        resolver.AddSearchDirectory(Path.GetDirectoryName(helperDllPath));

        var rp = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadWrite = true,
            InMemory = true
        };

        using (var assembly = AssemblyDefinition.ReadAssembly(dllPath, rp))
        {
            var module = assembly.MainModule;

            // 1. 添加对 MyModbusExtension.dll 的引用
            var helperAssembly = AssemblyDefinition.ReadAssembly(helperDllPath,
                new ReaderParameters { AssemblyResolver = resolver });
            var helperRef = module.AssemblyReferences.FirstOrDefault(
                r => r.Name == "MyModbusExtension");
            if (helperRef == null)
            {
                helperRef = new AssemblyNameReference(
                    helperAssembly.Name.Name, helperAssembly.Name.Version);
                module.AssemblyReferences.Add(helperRef);
            }

            // 2. 找到 ExternalRS232 类型
            var extRS232 = module.Types.FirstOrDefault(
                t => t.FullName == "EcoChemie.Communication.External.ExternalRS232");
            if (extRS232 == null)
            {
                Console.WriteLine("[错误] 找不到 ExternalRS232");
                return 1;
            }

            // 3. 导入 ModbusHelper 类型和方法
            var helperType = helperAssembly.MainModule.Types.FirstOrDefault(
                t => t.FullName == "MyModbusExtension.ModbusHelper");
            if (helperType == null)
            {
                Console.WriteLine("[错误] 找不到 ModbusHelper");
                return 1;
            }

            var helperTypeRef = module.ImportReference(helperType);

            // TrySendModbus(ExternalRS232 device, string command, out int bytesSent) : bool
            var trySendMethod = helperType.Methods.First(m =>
                m.Name == "TrySendModbus" && m.Parameters.Count == 3);
            var trySendRef = module.ImportReference(trySendMethod);

            // TryReceiveModbus(ExternalRS232 device) : string
            var tryReceiveMethod = helperType.Methods.First(m =>
                m.Name == "TryReceiveModbus" && m.Parameters.Count == 1);
            var tryReceiveRef = module.ImportReference(tryReceiveMethod);

            // 4. Patch Send 方法
            var sendMethod = extRS232.Methods.FirstOrDefault(m =>
                m.Name == "Send" &&
                m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == "System.String");
            if (sendMethod != null)
            {
                PatchSend(sendMethod, trySendRef);
                Console.WriteLine("[Patch] Send 方法已修改");
            }

            // 5. Patch Receive 方法
            var receiveMethod = extRS232.Methods.FirstOrDefault(m =>
                m.Name == "Receive" && m.Parameters.Count == 0);
            if (receiveMethod != null)
            {
                PatchReceive(receiveMethod, tryReceiveRef);
                Console.WriteLine("[Patch] Receive 方法已修改");
            }

            // 6. 保存（覆盖原文件）
            assembly.Write(dllPath);
            Console.WriteLine("[完成] EcoChemie100.dll 已更新");
            Console.WriteLine("       请重新启动 NOVA 生效。");
        }
        return 0;
    }

    static void PatchSend(MethodDefinition method, MethodReference trySendRef)
    {
        var body = method.Body;
        var il = body.GetILProcessor();

        // 清除旧方法体
        body.Instructions.Clear();
        body.Variables.Clear();

        // 需要局部变量：bool ok, int bytesSent
        var okVar = new VariableDefinition(method.Module.TypeSystem.Boolean);
        var bytesSentVar = new VariableDefinition(method.Module.TypeSystem.Int32);
        body.Variables.Add(okVar);
        body.Variables.Add(bytesSentVar);

        // --- 新 IL ---
        // ok = ModbusHelper.TrySendModbus(this, command, out bytesSent);
        il.Append(il.Create(OpCodes.Ldarg_0));          // this
        il.Append(il.Create(OpCodes.Ldarg_1));          // command
        il.Append(il.Create(OpCodes.Ldloca_S, bytesSentVar));
        il.Append(il.Create(OpCodes.Call, trySendRef));
        il.Append(il.Create(OpCodes.Stloc_0));          // ok

        // if (!ok) goto ORIGINAL;
        il.Append(il.Create(OpCodes.Ldloc_0));
        var origLabel = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Brfalse_S, origLabel));

        // return bytesSent;
        il.Append(il.Create(OpCodes.Ldloc_S, bytesSentVar));
        il.Append(il.Create(OpCodes.Ret));

        // ORIGINAL: 完全还原原始逻辑
        // this.get_SerialPort().WriteLine(command);
        // return command.Length;
        il.Append(origLabel);

        var getSerialPort = method.DeclaringType.Methods.First(m =>
            m.Name == "get_SerialPort" && m.Parameters.Count == 0);
        var writeLineMethod = method.Module.ImportReference(
            typeof(System.IO.Ports.SerialPort).GetMethod("WriteLine",
                new[] { typeof(string) }));
        var getLengthMethod = method.Module.ImportReference(
            typeof(string).GetProperty("Length").GetGetMethod());

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, getSerialPort));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Callvirt, writeLineMethod));

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Callvirt, getLengthMethod));
        il.Append(il.Create(OpCodes.Ret));
    }

    static void PatchReceive(MethodDefinition method, MethodReference tryReceiveRef)
    {
        var body = method.Body;
        var il = body.GetILProcessor();
        body.Instructions.Clear();
        body.Variables.Clear();

        // 局部变量：string result
        var resultVar = new VariableDefinition(method.Module.TypeSystem.String);
        body.Variables.Add(resultVar);

        // result = ModbusHelper.TryReceiveModbus(this);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, tryReceiveRef));
        il.Append(il.Create(OpCodes.Stloc_0));

        // if (result == null) goto ORIGINAL;
        il.Append(il.Create(OpCodes.Ldloc_0));
        var origLabel = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Brfalse_S, origLabel));

        // return result;
        il.Append(il.Create(OpCodes.Ldloc_0));
        il.Append(il.Create(OpCodes.Ret));

        // ORIGINAL: this.get_SerialPort().ReadLine();
        il.Append(origLabel);
        il.Append(il.Create(OpCodes.Ldarg_0));
        var getSerialPort = method.DeclaringType.Methods.First(m =>
            m.Name == "get_SerialPort" && m.Parameters.Count == 0);
        var readLineMethod = method.Module.ImportReference(
            typeof(System.IO.Ports.SerialPort).GetMethod("ReadLine"));
        il.Append(il.Create(OpCodes.Call, getSerialPort));
        il.Append(il.Create(OpCodes.Callvirt, readLineMethod));
        il.Append(il.Create(OpCodes.Ret));
    }
}
