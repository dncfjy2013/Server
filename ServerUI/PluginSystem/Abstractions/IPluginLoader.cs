using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Abstractions
{
    /// <summary>
    /// 插件加载器接口，负责发现和加载插件
    /// </summary>
    public interface IPluginLoader
    {
        /// <summary>
        /// 发现所有可用插件
        /// </summary>
        /// <returns>插件清单集合</returns>
        Task<IEnumerable<IPluginManifest>> DiscoverPluginsAsync();

        /// <summary>
        /// 加载插件程序集
        /// </summary>
        /// <param name="manifest">插件清单</param>
        /// <returns>插件类型</returns>
        Task<Type> LoadPluginAssemblyAsync(IPluginManifest manifest);

        void UnloadPluginAssembly(Guid pluginId);
    }
}    