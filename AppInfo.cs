using System.Reflection;

namespace ERHandlerManager
{
    public static class AppInfo
    {
        public static string Version =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }
}