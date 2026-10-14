using System;

namespace CoreHost
{
    /// <summary>
    /// 再計算ホストの入口（dotnet CoreHost.dll init|replay）。標準入力の JSON を読み、標準出力に JSON を書き、終了コードを返す。
    /// 処理は CoreHostRunner に任せる。標準入出力を開く処理も CoreHostRunner の例外の内側で行うので、ここで例外は外へ出ない（テストは Main を呼ばず、CoreHostRunner を直接呼ぶ）。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return CoreHostRunner.RunOnConsole(args, Console.OpenStandardInput, Console.OpenStandardOutput);
        }
    }
}
