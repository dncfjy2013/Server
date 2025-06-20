
using Server.Common;

namespace ExamplePlugin
{
    /// <summary>
    /// 示例插件实现，满足LoadPluginAssemblyAsync函数的加载要求
    /// </summary>
    public class ExamplePlugin : IPlugin
    {
        private readonly Guid _id;
        private readonly string _name;
        private readonly Version _version;
        private readonly string _description;
        private bool _isInitialized;
        private bool _isDisposed;

        // 无参数构造函数（如果需要通过反射激活）
        public ExamplePlugin()
        {
            // 从程序集属性获取元数据（替代自定义特性）
            _id = Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF");
            _name = "ExamplePlugin";
            _version = new Version("1.0.0");
            _description = "这是一个示例插件，演示基本的插件功能";
        }

        // 带参数构造函数（如果需要通过工厂注入元数据）
        public ExamplePlugin(Guid id, string name, Version version, string description)
        {
            _id = id;
            _name = name;
            _version = version;
            _description = description;
        }

        public Guid Id => _id;
        public string Name => _name;
        public Version Version => _version;
        public string Description => _description;

        public Task InitializeAsync()
        {
            if (_isInitialized)
                return Task.CompletedTask;

            Console.WriteLine($"初始化插件: {Name} ({Id}) v{Version}");
            // 模拟初始化操作
            _isInitialized = true;
            return Task.CompletedTask;
        }

        public Task<object> ExecuteAsync(object input)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("插件尚未初始化");

            Console.WriteLine($"插件 {Name} 处理输入: {input}");

            if (input is string text)
                return Task.FromResult<object>($"处理结果: {text.ToUpper()}");

            return Task.FromResult<object>($"不支持的输入类型: {input?.GetType().Name ?? "null"}");
        }

        public bool CanHandle(Type requestType)
        {
            return requestType == typeof(string);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                Console.WriteLine($"释放插件资源: {Name} ({Id})");
                _isInitialized = false;
            }

            _isDisposed = true;
        }
    }
}