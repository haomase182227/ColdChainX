using System;

class Program {
    static void Main() {
        string input1 = ""a\\\\b""; // this is literally 'a', '\', '\', 'b' in C#
        Console.WriteLine(input1);
        Console.WriteLine(input1.Replace(""\\\\"", ""\\"")); // replace '\\' with '\'
    }
}
