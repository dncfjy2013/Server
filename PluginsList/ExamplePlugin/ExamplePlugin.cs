
using Server.Common;

namespace ExamplePlugin
{
    /// <summary>
    /// ʾ�����ʵ�֣�����LoadPluginAssemblyAsync�����ļ���Ҫ��
    /// </summary>
    public class ExamplePlugin : IPlugin
    {
        private readonly Guid _id;
        private readonly string _name;
        private readonly Version _version;
        private readonly string _description;
        private bool _isInitialized;
        private bool _isDisposed;

        // �޲������캯���������Ҫͨ�����伤�
        public ExamplePlugin()
        {
            // �ӳ������Ի�ȡԪ���ݣ�����Զ������ԣ�
            _id = Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF");
            _name = "ExamplePlugin";
            _version = new Version("1.0.0");
            _description = "����һ��ʾ���������ʾ�����Ĳ������";
        }

        // ���������캯���������Ҫͨ������ע��Ԫ���ݣ�
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

            Console.WriteLine($"��ʼ�����: {Name} ({Id}) v{Version}");
            // ģ���ʼ������
            _isInitialized = true;
            return Task.CompletedTask;
        }

        public Task<object> ExecuteAsync(object input)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("�����δ��ʼ��");

            Console.WriteLine($"��� {Name} ��������: {input}");

            if (input is string text)
                return Task.FromResult<object>($"������: {text.ToUpper()}");

            return Task.FromResult<object>($"��֧�ֵ���������: {input?.GetType().Name ?? "null"}");
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
                Console.WriteLine($"�ͷŲ����Դ: {Name} ({Id})");
                _isInitialized = false;
            }

            _isDisposed = true;
        }
    }
}