using Server.Common;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Abstractions
{
    /// <summary>
    /// 插件管理器接口，负责插件的整体管理和调度
    /// </summary>
    public interface IPluginManager
    {
        /// <summary>
        /// 发现并加载所有插件
        /// </summary>
        /// <returns>加载成功的插件清单</returns>
        Task<IEnumerable<IPluginManifest>> InitializePluginsAsync();

        /// <summary>
        /// 执行特定插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <param name="input">输入参数</param>
        /// <returns>执行结果</returns>
        Task<object> ExecutePluginAsync(Guid pluginId, object input);

        /// <summary>
        /// 获取所有已加载的插件
        /// </summary>
        /// <returns>已加载插件清单集合</returns>
        IEnumerable<IPluginManifest> GetLoadedPlugins();

        /// <summary>
        /// 获取特定类型的插件
        /// </summary>
        /// <typeparam name="T">插件类型</typeparam>
        /// <returns>匹配的插件集合</returns>
        IEnumerable<T> GetPluginsOfType<T>() where T : class, IPlugin;

        /// <summary>
        /// 卸载特定插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>卸载操作是否成功</returns>
        Task<bool> UnloadPluginAsync(Guid pluginId);
    }
}    