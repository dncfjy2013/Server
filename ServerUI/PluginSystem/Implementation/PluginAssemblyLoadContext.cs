using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 插件程序集加载上下文，实现插件的隔离加载
    /// </summary>
    public sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginPath;

        public PluginAssemblyLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _pluginPath = pluginPath;
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // 尝试解析并加载程序集依赖
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            // 如果无法解析，则尝试从默认上下文加载
            // 这允许插件使用主应用程序中已加载的共享程序集
            return Assembly.Load(assemblyName);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            // 解析并加载非托管DLL
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// 从指定路径加载插件程序集
        /// </summary>
        /// <param name="assemblyPath">程序集路径</param>
        /// <returns>加载的程序集</returns>
        public new Assembly LoadFromAssemblyPath(string assemblyPath)
        {
            return base.LoadFromAssemblyPath(assemblyPath);
        }
    }
}
