using System;

namespace AutoExposingServiceFramework.Attributes
{
    /// <summary>
    /// 标记业务方法可被外部访问的特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class ExposeApiAttribute : Attribute
    {
        /// <summary>
        /// API路径（如：/business/operation）
        /// </summary>
        public string Path { get; }
    
        /// <summary>
        /// HTTP方法（GET/POST/PUT/DELETE）
        /// </summary>
        public string HttpMethod { get; set; } = "POST";
    
        /// <summary>
        /// 接口描述（用于Swagger文档）
        /// </summary>
        public string Description { get; set; } = "";

        public ExposeApiAttribute(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }
    }
}
