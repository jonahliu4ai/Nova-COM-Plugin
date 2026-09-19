using System;
class A {
    public static void Main() {
        Func<int,int> f = x=>x+1;
        Console.WriteLine(f(1));
        int? n=null;
        Console.WriteLine(n ?? 0);
        Console.WriteLine("abc");
    }
}
