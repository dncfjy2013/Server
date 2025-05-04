using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Enums
{
    /// <summary>
    /// 该枚举定义了服务器可能出现的各种错误码，用于统一管理和识别不同类型的错误。
    /// </summary>
    public enum ErrorCode
    {
        /// <summary>
        /// 操作成功，没有出现任何错误。
        /// </summary>
        Success = 0,

        // 网络相关错误
        /// <summary>
        /// 网络请求超时，可能由于网络不稳定、服务器响应过慢等原因导致。
        /// </summary>
        NetworkTimeout = 1001,
        /// <summary>
        /// 无法连接到数据库，可能是数据库服务未启动、连接字符串配置错误等原因。
        /// </summary>
        DatabaseConnectionFailed = 1002,
        /// <summary>
        /// 服务器无法访问，可能是服务器未启动、网络故障或服务器地址错误。
        /// </summary>
        ServerUnreachable = 1003,
        /// <summary>
        /// 套接字操作出现错误，例如连接被拒绝、连接丢失等。
        /// </summary>
        SocketError = 1004,
        /// <summary>
        /// DNS 解析失败，无法将域名转换为 IP 地址。
        /// </summary>
        DNSResolutionFailed = 1005,

        // 数据验证错误
        /// <summary>
        /// 输入的数据不符合要求，可能是数据类型、长度等不符合规定。
        /// </summary>
        InvalidInput = 2001,
        /// <summary>
        /// 请求中缺少必要的参数，导致无法完成操作。
        /// </summary>
        MissingRequiredParameter = 2002,
        /// <summary>
        /// 输入的日期格式不正确，无法被系统正确解析。
        /// </summary>
        InvalidDateFormat = 2003,
        /// <summary>
        /// 输入的电子邮件地址格式不符合标准。
        /// </summary>
        InvalidEmailFormat = 2004,
        /// <summary>
        /// 输入的电话号码格式不正确。
        /// </summary>
        InvalidPhoneNumber = 2005,
        /// <summary>
        /// 输入的密码不符合密码强度要求或与现有密码不匹配。
        /// </summary>
        InvalidPassword = 2006,
        /// <summary>
        /// 输入的 JSON 数据格式不正确，无法被系统正确解析。
        /// </summary>
        InvalidJSONFormat = 2007,
        /// <summary>
        /// 输入的 XML 数据格式不正确，无法被系统正确解析。
        /// </summary>
        InvalidXMLFormat = 2008,

        // 权限错误
        /// <summary>
        /// 用户未经过授权，尝试访问需要权限的资源或执行需要权限的操作。
        /// </summary>
        UnauthorizedAccess = 3001,
        /// <summary>
        /// 用户的权限不足，无法完成所请求的操作。
        /// </summary>
        InsufficientPermissions = 3002,
        /// <summary>
        /// 用户的令牌已过期，需要重新获取有效的令牌。
        /// </summary>
        TokenExpired = 3003,
        /// <summary>
        /// 用户提供的令牌无效，可能是令牌被篡改或格式错误。
        /// </summary>
        TokenInvalid = 3004,
        /// <summary>
        /// 用户账户被锁定，可能是由于多次尝试登录失败等原因。
        /// </summary>
        AccountLocked = 3005,

        // 服务器内部错误
        /// <summary>
        /// 服务器内部出现未知错误，可能是代码逻辑错误、配置错误等原因。
        /// </summary>
        InternalServerError = 5001,
        /// <summary>
        /// 服务器资源耗尽，例如内存、CPU 等资源不足。
        /// </summary>
        ResourceExhausted = 5002,
        /// <summary>
        /// 服务器内存不足，无法分配足够的内存来完成操作。
        /// </summary>
        OutOfMemory = 5003,
        /// <summary>
        /// 请求的文件不存在，可能是文件被删除或路径错误。
        /// </summary>
        FileNotFound = 5004,
        /// <summary>
        /// 请求的目录不存在，可能是目录被删除或路径错误。
        /// </summary>
        DirectoryNotFound = 5005,
        /// <summary>
        /// 数据库查询操作出现错误，可能是 SQL 语句错误、数据库表结构变更等原因。
        /// </summary>
        DatabaseQueryError = 5006,
        /// <summary>
        /// 数据库更新操作失败，可能是数据冲突、权限不足等原因。
        /// </summary>
        DatabaseUpdateFailed = 5007,
        /// <summary>
        /// 数据库插入操作失败，可能是数据重复、字段约束等原因。
        /// </summary>
        DatabaseInsertFailed = 5008,
        /// <summary>
        /// 数据库删除操作失败，可能是数据关联、权限不足等原因。
        /// </summary>
        DatabaseDeleteFailed = 5009,

        // 业务逻辑错误
        /// <summary>
        /// 请求的用户不存在，可能是用户 ID 错误或用户已被删除。
        /// </summary>
        UserNotFound = 6001,
        /// <summary>
        /// 请求的产品不存在，可能是产品 ID 错误或产品已下架。
        /// </summary>
        ProductNotFound = 6002,
        /// <summary>
        /// 请求的订单不存在，可能是订单 ID 错误或订单已被取消。
        /// </summary>
        OrderNotFound = 6003,
        /// <summary>
        /// 尝试使用的用户名已被其他用户使用，不允许重复。
        /// </summary>
        DuplicateUsername = 6004,
        /// <summary>
        /// 尝试使用的电子邮件地址已被其他用户注册，不允许重复。
        /// </summary>
        DuplicateEmail = 6005,
        /// <summary>
        /// 商品库存不足，无法完成订单。
        /// </summary>
        InsufficientStock = 6006,
        /// <summary>
        /// 支付操作失败，可能是支付方式错误、账户余额不足等原因。
        /// </summary>
        PaymentFailed = 6007,
        /// <summary>
        /// 使用的优惠券已过期，无法继续使用。
        /// </summary>
        CouponExpired = 6008,
        /// <summary>
        /// 使用的优惠券无效，可能是优惠券代码错误或已被使用。
        /// </summary>
        CouponInvalid = 6009
    }
}
