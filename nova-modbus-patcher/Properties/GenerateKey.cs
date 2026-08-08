// GenerateKey.cs
using System;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;

[assembly: AssemblyCompany("S.Liu Studio")]
[assembly: AssemblyCopyright("Copyright © 2026 S.Liu Studio")]
[assembly: AssemblyVersion("0.0.1.0")]      // 运行时版本
[assembly: AssemblyFileVersion("0.0.1.0")]   // 文件版本（资源管理器里看到的）
class Program
{
    static void Main()
    {
        var rsa = new RSACryptoServiceProvider(2048);
        File.WriteAllBytes("MyKey.snk", rsa.ExportCspBlob(true));
        Console.WriteLine("MyKey.snk created");
    }
}