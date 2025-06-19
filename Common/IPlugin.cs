using System;
using System.Threading.Tasks;

namespace Server.Common
{
    /// <summary>
    /// 插件基础接口，定义插件的基本行为和属性
    /// </summary>
    public interface IPlugin : IDisposable
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
        /// 插件描述信息
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 初始化插件
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 执行插件主逻辑
        /// </summary>
        /// <param name="input">输入参数</param>
        /// <returns>执行结果</returns>
        Task<object> ExecuteAsync(object input);

        /// <summary>
        /// 检查插件是否可以处理特定类型的请求
        /// </summary>
        /// <param name="requestType">请求类型</param>
        /// <returns>是否可以处理</returns>
        bool CanHandle(Type requestType);
    }
}    