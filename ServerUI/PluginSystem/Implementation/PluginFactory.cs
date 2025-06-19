using Server.Common;
using ServerUI.PluginSystem.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 插件工厂实现，负责创建和管理插件实例
    /// </summary>
    public sealed class PluginFactory : IPluginFactory
    {
        private readonly ConcurrentDictionary<Guid, IPlugin> _loadedPlugins = new();
        private readonly IServiceProvider _serviceProvider;
        private readonly IPluginLoader _pluginLoader;

        public PluginFactory(IServiceProvider serviceProvider, IPluginLoader pluginLoader)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        }

        /// <summary>
        /// 创建并初始化插件实例
        /// </summary>
        /// <param name="manifest">插件清单</param>
        /// <returns>插件实例</returns>
        public async Task<IPlugin> CreatePluginAsync(IPluginManifest manifest)
        {
            if (_loadedPlugins.TryGetValue(manifest.Id, out var existingPlugin))
                return existingPlugin;

            var pluginType = await _pluginLoader.LoadPluginAssemblyAsync(manifest);

            // 尝试使用带参数的构造函数
            var plugin = Activator.CreateInstance(
                pluginType,
                manifest.Id,
                manifest.Name,
                manifest.Version,
                manifest.Description) as IPlugin;

            if (plugin == null)
            {
                // 尝试使用无参数构造函数
                plugin = Activator.CreateInstance(pluginType) as IPlugin;
            }

            if (plugin == null)
                throw new InvalidOperationException($"无法实例化插件类型: {manifest.EntryType}");

            if (_loadedPlugins.TryAdd(manifest.Id, plugin))
            {
                await plugin.InitializeAsync();
                return plugin;
            }

            throw new InvalidOperationException($"插件已存在: {manifest.Name} ({manifest.Id})");
        }

        /// <summary>
        /// 获取已加载的插件实例
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>插件实例，如果未加载则返回null</returns>
        public IPlugin GetPlugin(Guid pluginId)
        {
            _loadedPlugins.TryGetValue(pluginId, out var plugin);
            return plugin;
        }

        /// <summary>
        /// 检查插件是否已加载
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>是否已加载</returns>
        public bool IsPluginLoaded(Guid pluginId)
        {
            return _loadedPlugins.ContainsKey(pluginId);
        }

        /// <summary>
        /// 释放插件资源并卸载插件
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>卸载操作是否成功</returns>
        public bool UnloadPlugin(Guid pluginId)
        {
            if (_loadedPlugins.TryRemove(pluginId, out var plugin))
            {
                plugin.Dispose();
                return true;
            }

            return false;
        }

        private async Task<Type> LoadPluginTypeAsync(IPluginManifest manifest)
        {
            // 实际实现中，这里会使用Assembly.LoadFrom或AssemblyLoadContext加载插件程序集
            // 为简化示例，这里假设插件类型已经通过某种方式加载
            await Task.CompletedTask;
            
            // 在实际应用中，这里需要根据manifest中的信息加载正确的程序集和类型
            var assembly = Assembly.GetExecutingAssembly();
            var type = assembly.GetType(manifest.EntryType);
            
            if (type == null)
                throw new TypeLoadException($"无法加载插件类型: {manifest.EntryType}");
                
            return type;
        }

        private IPlugin CreatePluginInstance(Type pluginType, IPluginManifest manifest)
        {
            // 使用依赖注入容器创建实例，支持构造函数注入
            try
            {
                if (Activator.CreateInstance(pluginType, _serviceProvider) is IPlugin plugin)
                    return plugin;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"创建插件实例失败: {manifest.Name}", ex);
            }

            throw new InvalidOperationException($"插件类型 {pluginType.FullName} 未实现 IPlugin 接口");
        }
    }
}    