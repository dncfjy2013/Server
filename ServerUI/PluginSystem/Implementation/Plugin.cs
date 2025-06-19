using Server.Common;
using ServerUI.PluginSystem.Abstractions;
using System;
using System.Threading.Tasks;

namespace ServerUI.PluginSystem.Implementation
{
    /// <summary>
    /// 插件基类，实现了IPlugin接口的基本功能
    /// </summary>
    public abstract class Plugin : IPlugin
    {
        private bool _isDisposed;
        private bool _isInitialized;

        /// <summary>
        /// 插件唯一标识
        /// </summary>
        public abstract Guid Id { get; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 插件版本
        /// </summary>
        public abstract Version Version { get; }

        /// <summary>
        /// 插件描述信息
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await OnInitializeAsync();
            _isInitialized = true;
        }

        /// <summary>
        /// 当插件初始化时调用
        /// </summary>
        protected abstract Task OnInitializeAsync();

        /// <summary>
        /// 执行插件主逻辑
        /// </summary>
        /// <param name="input">输入参数</param>
        /// <returns>执行结果</returns>
        public async Task<object> ExecuteAsync(object input)
        {
            EnsureInitialized();
            return await OnExecuteAsync(input);
        }

        /// <summary>
        /// 当执行插件时调用
        /// </summary>
        /// <param name="input">输入参数</param>
        /// <returns>执行结果</returns>
        protected abstract Task<object> OnExecuteAsync(object input);

        /// <summary>
        /// 检查插件是否可以处理特定类型的请求
        /// </summary>
        /// <param name="requestType">请求类型</param>
        /// <returns>是否可以处理</returns>
        public abstract bool CanHandle(Type requestType);

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源的实现
        /// </summary>
        /// <param name="disposing">是否正在进行显式释放</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                // 释放托管资源
                OnDispose();
            }

            // 释放非托管资源

            _isDisposed = true;
        }

        /// <summary>
        /// 当释放资源时调用
        /// </summary>
        protected virtual void OnDispose()
        {
            // 默认实现为空
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("插件尚未初始化");
        }
    }
}    