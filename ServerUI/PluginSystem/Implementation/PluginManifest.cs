using ServerUI.PluginSystem.Abstractions;
using System;
using System.Collections.Generic;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 插件清单实现
    /// </summary>
    public sealed class PluginManifest : IPluginManifest
    {
        /// <summary>
        /// 插件唯一标识
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 插件版本
        /// </summary>
        public Version Version { get; set; }

        /// <summary>
        /// 插件作者
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// 插件描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 插件入口类型
        /// </summary>
        public string EntryType { get; set; }

        /// <summary>
        /// 插件依赖项
        /// </summary>
        public IEnumerable<PluginDependency> Dependencies { get; set; } = Array.Empty<PluginDependency>();
    }
}    