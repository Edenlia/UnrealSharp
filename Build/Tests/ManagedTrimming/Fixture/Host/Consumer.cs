namespace TestHost;

public static class Consumer
{
    public static int ReadGameDependency() => Game.PluginDependency.Read();
}
