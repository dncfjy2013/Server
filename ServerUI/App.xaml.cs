using System.Configuration;
using System.Data;
using System.Windows;
using ServerUI.Views;

namespace ServerUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<LeftSidebar>();
            containerRegistry.RegisterForNavigation<RightSidebar>();
        }
    }
}
