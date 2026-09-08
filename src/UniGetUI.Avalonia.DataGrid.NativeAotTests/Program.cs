namespace UniGetUI.Avalonia.DataGrid.NativeAotTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify boundaries or ui, with --expect-aot for native acceptance.");
            return 2;
        }

        // Each command runs in its own process so Avalonia is initialized exactly once.
        return args[0] switch
        {
            "boundaries" => DataGridAotRepro.Program.Run(args[1..]),
            "ui" => Acceptance.Program.Run(args[1..]),
            _ => 2,
        };
    }
}
