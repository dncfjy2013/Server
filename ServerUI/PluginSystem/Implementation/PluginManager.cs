using Server.Common;
using ServerUI.PluginSystem.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 插件管理器实现，负责插件的整体管理和调度
    /// </summary>
    public sealed class PluginManager : IPluginManager
    {
        private readonly IPluginLoader _pluginLoader;
        private readonly IPluginFactory _pluginFactory;
        private readonly Dictionary<Guid, IPluginManifest> _loadedPlugins = new();

        public PluginManager(IPluginLoader pluginLoader, IPluginFactory pluginFactory)
        {
            _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
            _pluginFactory = pluginFactory ?? throw new ArgumentNullException(nameof(pluginFactory));
        }

        /// <summary>
        /// 发现并加载所有插件
        /// </summary>
        /// <returns>加载成功的插件清单</returns>
        public async Task<IEnumerable<IPluginManifest>> InitializePluginsAsync()
        {
            var manifests = await _pluginLoader.DiscoverPluginsAsync();
            var loadedManifests = new List<IPluginManifest>();

            foreach (var manifest in manifests)
            {
                try
                {
                    // 检查依赖项
                    if (!await CheckDependenciesAsync(manifest))
                    {
                        Console.WriteLine($"插件 {manifest.Name} 依赖项不满足，跳过加载");
                        continue;
                    }

                    // 创建并初始化插件
                    var plugin = await _pluginFactory.CreatePluginAsync(manifest);
                    _loadedPlugins[manifest.Id] = manifest;
                    loadedManifests.Add(manifest);
                    Console.WriteLine($"插件 {manifest.Name} ({manifest.Id}) 加载成功");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载插件 {manifest.Name} 失败: {ex.Message}");
                }
            }

            return loadedManifests;
        }

        /// <summary>
        /// 执行特定插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <param name="input">输入参数</param>
        /// <returns>执行结果</returns>
        public async Task<object> ExecutePluginAsync(Guid pluginId, object input)
        {
            if (!_loadedPlugins.TryGetValue(pluginId, out var manifest))
                throw new ArgumentException($"未找到插件: {pluginId}");

            var plugin = _pluginFactory.GetPlugin(pluginId);
            if (plugin == null)
                throw new InvalidOperationException($"插件已加载但实例不可用: {manifest.Name}");

            return await plugin.ExecuteAsync(input);
        }

        /// <summary>
        /// 获取所有已加载的插件
        /// </summary>
        /// <returns>已加载插件清单集合</returns>
        public IEnumerable<IPluginManifest> GetLoadedPlugins()
        {
            return _loadedPlugins.Values;
        }

        /// <summary>
        /// 获取特定类型的插件
        /// </summary>
        /// <typeparam name="T">插件类型</typeparam>
        /// <returns>匹配的插件集合</returns>
        public IEnumerable<T> GetPluginsOfType<T>() where T : class, IPlugin
        {
            return _loadedPlugins.Keys
                .Select(pluginId => _pluginFactory.GetPlugin(pluginId))
                .OfType<T>();
        }

        /// <summary>
        /// 卸载特定插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>卸载操作是否成功</returns>
        public async Task<bool> UnloadPluginAsync(Guid pluginId)
        {
            if (!_loadedPlugins.ContainsKey(pluginId))
                return false;

            var result = _pluginFactory.UnloadPlugin(pluginId);
            if (result)
                _loadedPlugins.Remove(pluginId);

            return result;
        }

        private async Task<bool> CheckDependenciesAsync(IPluginManifest manifest)
        {
            // 获取所有已加载的插件
            var loadedPlugins = await _pluginLoader.DiscoverPluginsAsync();
            var loadedPluginDict = loadedPlugins.ToDictionary(p => p.Id);

            // 检查每个依赖项
            foreach (var dependency in manifest.Dependencies)
            {
                if (!loadedPluginDict.TryGetValue(dependency.PluginId, out var loadedPlugin))
                {
                    Console.WriteLine($"插件 {manifest.Name} 依赖的插件 {dependency.PluginId} 未加载");
                    return false;
                }

                if (loadedPlugin.Version < dependency.MinimumVersion)
                {
                    Console.WriteLine($"插件 {manifest.Name} 依赖的插件 {dependency.PluginId} 版本过低，需要 {dependency.MinimumVersion}，实际 {loadedPlugin.Version}");
                    return false;
                }
            }

            return true;
        }
    }
}    