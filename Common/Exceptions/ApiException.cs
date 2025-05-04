using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Exceptions
{
    /// <summary>
    /// 自定义的 API 异常类，继承自 Exception 类，用于在服务器端处理 API 调用过程中出现的异常情况。
    /// 该类包含错误码和错误详情信息，方便客户端根据错误码进行相应处理，并能获取详细的错误描述。
    /// </summary>
    public class ApiException : Exception
    {
        /// <summary>
        /// 获取异常对应的错误码，用于标识不同类型的错误。
        /// </summary>
        public int ErrorCode { get; }

        /// <summary>
        /// 获取异常的详细错误信息，可为空。该信息可用于更详细地描述错误发生的原因。
        /// </summary>
        public string ErrorDetails { get; }

        /// <summary>
        /// 初始化 ApiException 类的新实例。
        /// </summary>
        /// <param name="errorCode">错误码，用于唯一标识不同类型的错误。</param>
        /// <param name="message">错误消息，简要描述错误的性质。</param>
        public ApiException(int errorCode, string message) : base(message)
        {
            // 将传入的错误码赋值给 ErrorCode 属性
            ErrorCode = errorCode;
            // 由于未提供错误详情，将 ErrorDetails 属性置为 null
            ErrorDetails = null;
        }

        /// <summary>
        /// 初始化 ApiException 类的新实例，包含详细的错误信息。
        /// </summary>
        /// <param name="errorCode">错误码，用于唯一标识不同类型的错误。</param>
        /// <param name="message">错误消息，简要描述错误的性质。</param>
        /// <param name="errorDetails">详细的错误信息，用于更深入地说明错误原因。</param>
        public ApiException(int errorCode, string message, string errorDetails) : base(message)
        {
            // 将传入的错误码赋值给 ErrorCode 属性
            ErrorCode = errorCode;
            // 将传入的详细错误信息赋值给 ErrorDetails 属性
            ErrorDetails = errorDetails;
        }

        /// <summary>
        /// 初始化 ApiException 类的新实例，并关联内部异常。
        /// </summary>
        /// <param name="errorCode">错误码，用于唯一标识不同类型的错误。</param>
        /// <param name="message">错误消息，简要描述错误的性质。</param>
        /// <param name="innerException">引发当前异常的内部异常。</param>
        public ApiException(int errorCode, string message, Exception innerException) : base(message, innerException)
        {
            // 将传入的错误码赋值给 ErrorCode 属性
            ErrorCode = errorCode;
            // 由于未提供错误详情，将 ErrorDetails 属性置为 null
            ErrorDetails = null;
        }

        /// <summary>
        /// 初始化 ApiException 类的新实例，包含详细错误信息并关联内部异常。
        /// </summary>
        /// <param name="errorCode">错误码，用于唯一标识不同类型的错误。</param>
        /// <param name="message">错误消息，简要描述错误的性质。</param>
        /// <param name="errorDetails">详细的错误信息，用于更深入地说明错误原因。</param>
        /// <param name="innerException">引发当前异常的内部异常。</param>
        public ApiException(int errorCode, string message, string errorDetails, Exception innerException) : base(message, innerException)
        {
            // 将传入的错误码赋值给 ErrorCode 属性
            ErrorCode = errorCode;
            // 将传入的详细错误信息赋值给 ErrorDetails 属性
            ErrorDetails = errorDetails;
        }
    }
}
