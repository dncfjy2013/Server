using Server.Common;
using System;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Abstractions
{
    /// <summary>
    /// 插件工厂接口，负责创建和管理插件实例
    /// </summary>
    public interface IPluginFactory
    {
        /// <summary>
        /// 创建并初始化插件实例
        /// </summary>
        /// <param name="manifest">插件清单</param>
        /// <returns>插件实例</returns>
        Task<IPlugin> CreatePluginAsync(IPluginManifest manifest);

        /// <summary>
        /// 获取已加载的插件实例
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>插件实例，如果未加载则返回null</returns>
        IPlugin GetPlugin(Guid pluginId);

        /// <summary>
        /// 检查插件是否已加载
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>是否已加载</returns>
        bool IsPluginLoaded(Guid pluginId);

        /// <summary>
        /// 释放插件资源并卸载插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>卸载操作是否成功</returns>
        bool UnloadPlugin(Guid pluginId);
    }
}    