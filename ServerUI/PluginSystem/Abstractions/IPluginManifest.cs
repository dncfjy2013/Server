using System;
using System.Collections.Generic;

namespace ServerUI.PluginSystem.Abstractions
{
    /// <summary>
    /// 插件清单接口，提供插件元数据信息
    /// </summary>
    public interface IPluginManifest
    {
        /// <summary>
        /// 插件唯一标识
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// 插件名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 插件版本
        /// </summary>
        Version Version { get; }

        /// <summary>
        /// 插件作者
        /// </summary>
        string Author { get; }

        /// <summary>
        /// 插件描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 插件入口类型
        /// </summary>
        string EntryType { get; }

        /// <summary>
        /// 插件依赖项
        /// </summary>
        IEnumerable<PluginDependency> Dependencies { get; }
    }

    /// <summary>
    /// 插件依赖项结构
    /// </summary>
    public sealed class PluginDependency
    {
        /// <summary>
        /// 依赖插件ID
        /// </summary>
        public Guid PluginId { get; set; }

        /// <summary>
        /// 依赖的最低版本
        /// </summary>
        public Version MinimumVersion { get; set; }
    }
}    