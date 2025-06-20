using Microsoft.Extensions.DependencyInjection;
using ServerUI.PluginSystem.Abstractions;
using ServerUI.PluginSystem.Implementation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ServerUI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //Main();
        }

        static async Task Main()
        {
            // 配置依赖注入
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            // 获取插件管理器
            var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

            // 初始化并加载所有插件
            var loadedPlugins = await pluginManager.InitializePluginsAsync();
            Console.WriteLine($"已加载 {loadedPlugins.Count()} 个插件");

            // 执行特定插件
            foreach (var plugin in loadedPlugins)
            {
                Console.WriteLine($"执行插件: {plugin.Name} ({plugin.Id})");
                var result = await pluginManager.ExecutePluginAsync(plugin.Id, "测试输入");
                Console.WriteLine($"插件返回: {result}");
            }

            Console.WriteLine("按任意键退出...");
            foreach (var plugin in loadedPlugins)
            {
                Console.WriteLine($"执行插件: {plugin.Name} ({plugin.Id})");
                var result = await pluginManager.UnloadPluginAsync(plugin.Id);
                Console.WriteLine($"插件返回: {result}");
            }
            Console.ReadKey();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // 注册插件系统服务
            services.AddSingleton<IPluginLoader>(new FileSystemPluginLoader("./Plugins"));
            services.AddSingleton<IPluginFactory, PluginFactory>();
            services.AddSingleton<IPluginManager, PluginManager>();

            // 注册应用程序服务
            // services.AddTransient<IMyService, MyService>();
        }
    }
}