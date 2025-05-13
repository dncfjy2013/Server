using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Text;
using Oracle.ManagedDataAccess.Client;
using MySql.Data.MySqlClient;
using Npgsql;
using Server.DataBase.Common;

// 定义支持的数据库类型
public enum DatabaseType
{
    SqlServer,
    MySql,
    PostgreSQL,
    Oracle
    // 可以按需添加更多数据库类型
}

// 通用数据库配置类
public class DatabaseConfig: BaseConfig
{
    // 数据库类型
    public DatabaseType DatabaseType { get; set; } = DatabaseType.SqlServer;

    // 基础配置
    public string Host { get; set; } = "localhost";
    public int Port { get; set; }
    public string DatabaseName { get; set; } = "master";

    // 认证配置
    public string UserId { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IntegratedSecurity { get; set; }

    // 安全配置
    public bool Encrypt { get; set; }
    public bool TrustServerCertificate { get; set; }

    // 连接配置
    public int ConnectionTimeout { get; set; } = 15;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 0;
    public int ConnectRetryCount { get; set; } = 3;
    public int ConnectRetryInterval { get; set; } = 10;

    // 应用配置
    public string ApplicationName { get; set; } = "MyApp";
    public bool ReadOnly { get; set; }
    public string Schema { get; set; } = "dbo";

    // 高级配置
    public int CommandTimeout { get; set; } = 30;
    public bool MultipleActiveResultSets { get; set; }
    public bool PersistSecurityInfo { get; set; }

    // 扩展配置
    public Dictionary<string, string> ExtendedProperties { get; set; } = new Dictionary<string, string>
    {
        {"ColumnEncryptionSetting", "Disabled"},
        {"AttachDBFilename", ""}
    };

    // 连接字符串缓存
    private string? _connectionString;
    public string ConnectionString
    {
        get => _connectionString ?? BuildConnectionString();
        set => _connectionString = value;
    }

    // 构建连接字符串的方法
    private string BuildConnectionString()
    {
        switch (DatabaseType)
        {
            case DatabaseType.SqlServer:
                return BuildSqlServerConnectionString();
            case DatabaseType.MySql:
                return BuildMySqlConnectionString();
            case DatabaseType.PostgreSQL:
                return BuildPostgreSQLConnectionString();
            case DatabaseType.Oracle: // 添加 Oracle 数据库类型的处理
                return BuildOracleConnectionString();
            default:
                throw new NotSupportedException($"Database type {DatabaseType} is not supported.");
        }
    }

    // 构建 SQL Server 连接字符串
    private string BuildSqlServerConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{Host},{Port}",
            InitialCatalog = DatabaseName,
            UserID = UserId,
            Password = Password,
            IntegratedSecurity = IntegratedSecurity,
            Encrypt = Encrypt,
            TrustServerCertificate = TrustServerCertificate,
            ConnectTimeout = ConnectionTimeout,
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = MinPoolSize,
            ConnectRetryCount = ConnectRetryCount,
            ConnectRetryInterval = ConnectRetryInterval,
            ApplicationName = ApplicationName,
            ApplicationIntent = ReadOnly ? Microsoft.Data.SqlClient.ApplicationIntent.ReadOnly : Microsoft.Data.SqlClient.ApplicationIntent.ReadWrite,
            MultipleActiveResultSets = MultipleActiveResultSets,
            PersistSecurityInfo = PersistSecurityInfo
        };

        // 合并扩展属性
        foreach (var kvp in ExtendedProperties)
        {
            builder[kvp.Key] = kvp.Value;
        }

        return builder.ToString();
    }

    // 构建 MySQL 连接字符串
    private string BuildMySqlConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Host,
            Port = (uint)Port,
            UserID = UserId,
            Password = Password,
            Database = DatabaseName,
            ConnectionTimeout = (uint)ConnectionTimeout,
            MinimumPoolSize = (uint)MinPoolSize,
            MaximumPoolSize = (uint)MaxPoolSize,
            SslMode = Encrypt ? MySqlSslMode.Required : MySqlSslMode.None,
        };

        // 处理扩展属性
        foreach (var kvp in ExtendedProperties)
        {
            try
            {
                // 动态设置其他属性
                builder[kvp.Key] = kvp.Value;
            }
            catch (ArgumentException)
            {
                // 忽略不支持的属性
            }
        }

        return builder.ConnectionString;
    }

    // 构建 PostgreSQL 连接字符串
    private string BuildPostgreSQLConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = DatabaseName,
            Username = UserId,
            Password = Password,
            Timeout = ConnectionTimeout,
            // 添加其他常用配置
            Pooling = true,
            MinPoolSize = MinPoolSize,
            MaxPoolSize = MaxPoolSize,
            CommandTimeout = CommandTimeout,
            SslMode = Encrypt ? SslMode.Require : SslMode.Disable,
            TrustServerCertificate = TrustServerCertificate
        };

        // 合并扩展属性
        foreach (var kvp in ExtendedProperties)
        {
            builder[kvp.Key] = kvp.Value;
        }

        return builder.ConnectionString;
    }

    // 构建 Oracle 连接字符串
    private string BuildOracleConnectionString()
    {
        var builder = new OracleConnectionStringBuilder
        {
            DataSource = $"{Host}:{Port}/{DatabaseName}",
            UserID = UserId,
            Password = Password,
            ConnectionTimeout = ConnectionTimeout,
            MinPoolSize = MinPoolSize,
            MaxPoolSize = MaxPoolSize
        };

        // 合并扩展属性
        foreach (var kvp in ExtendedProperties)
        {
            builder.Add(kvp.Key, kvp.Value);
        }

        return builder.ToString();
    }
}