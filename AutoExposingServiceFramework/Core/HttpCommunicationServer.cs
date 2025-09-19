using AutoExposingServiceFramework.Attributes;
using AutoExposingServiceFramework.Interfaces;
using AutoExposingServiceFramework.Models;
using AutoExposingServiceFramework.Models.Configs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoExposingServiceFramework.Core;

/// <summary>
/// HTTP通信服务（自动发现并注册接口）
/// </summary>
public class HttpCommunicationServer : ICommunicationServer
{
    private readonly ILogger _logger;
    private readonly HttpServerConfig _config;
    private readonly IBusinessWorker _businessWorker;
    private readonly List<ApiMetadata> _exposedApis;
    private WebApplication? _webApplication;

    public string ServerName => "HTTP API Server";
    public bool IsRunning => _webApplication != null;

    public HttpCommunicationServer(ILogger logger,
                                  HttpServerConfig config,
                                  IBusinessWorker businessWorker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _businessWorker = businessWorker ?? throw new ArgumentNullException(nameof(businessWorker));
        _exposedApis = DiscoverExposedApis(businessWorker.GetType());
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            _logger.LogWarning("HTTP服务已在运行中");
            return Task.CompletedTask;
        }

        // 创建Web应用构建器 (.NET 8 推荐方式)
        var builder = WebApplication.CreateBuilder();

        // 配置监听地址
        builder.WebHost.UseUrls(_config.ListenUrls);

        // 配置服务
        ConfigureServices(builder.Services);

        // 构建应用并配置中间件
        var app = builder.Build();
        ConfigureMiddleware(app);

        // 启动应用
        _ = app.RunAsync(cancellationToken);
        _webApplication = app;

        _logger.Log($"HTTP服务已启动，监听地址: {_config.ListenUrls}");
        _logger.Log($"自动注册接口数量: {_exposedApis.Count}");
        foreach (var api in _exposedApis)
        {
            _logger.Log($"  - {api.HttpMethod} {api.Path}");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_webApplication != null)
        {
            // 修正：确保配置类中有ShutdownTimeoutSeconds属性，或使用默认值
            var timeoutSeconds = _config.ShutdownTimeoutSeconds > 0
                ? _config.ShutdownTimeoutSeconds
                : 30; // 默认30秒超时

            var cancellationToken = new CancellationTokenSource(
                TimeSpan.FromSeconds(timeoutSeconds)).Token;

            try
            {
                await _webApplication.StopAsync(cancellationToken);
                await _webApplication.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"HTTP服务停止出错: {ex.Message}");
            }
            finally
            {
                _webApplication = null;
                _logger.Log("HTTP服务已停止");
            }
        }
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    private void ConfigureServices(IServiceCollection services)
    {
        if (_config.EnableSwagger)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "自动暴露接口",
                    Version = "v1",
                    Description = "基于.NET 8的自动接口暴露框架"
                });
            });
        }

        // 配置JSON序列化选项 (.NET 8 适配)
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.WriteIndented = true;
            options.SerializerOptions.AllowTrailingCommas = true;
            options.SerializerOptions.IgnoreNullValues = false;
            // 修正：使用正确的枚举转换器
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
    }

    /// <summary>
    /// 配置中间件
    /// </summary>
    private void ConfigureMiddleware(WebApplication app)
    {
        // 增加异常处理中间件，增强容错性
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                _logger.LogError($"请求处理出错: {ex.Message}（堆栈：{ex.StackTrace}）");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object>.ErrorResult("服务器内部错误"));
            }
        });

        if (_config.EnableSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "自动暴露接口 v1");
                c.RoutePrefix = string.Empty;
            });
        }

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            RegisterExposedApis(endpoints);
            RegisterHealthCheck(endpoints);
        });
    }

    /// <summary>
    /// 发现所有标记了ExposeApiAttribute的方法
    /// </summary>
    private List<ApiMetadata> DiscoverExposedApis(Type workerType)
    {
        var apis = new List<ApiMetadata>();

        foreach (var method in workerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = method.GetCustomAttribute<ExposeApiAttribute>();
            if (attr != null)
            {
                // 验证方法是否为异步方法
                if (!typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    _logger.LogWarning($"方法 {method.Name} 标记了ExposeApi，但不是异步方法(返回Task)，已忽略");
                    continue;
                }

                apis.Add(new ApiMetadata
                {
                    Path = attr.Path,
                    HttpMethod = attr.HttpMethod.ToUpper(),
                    Description = attr.Description,
                    Method = method,
                    ParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray(),
                    ReturnType = method.ReturnType
                });
            }
        }

        return apis;
    }

    /// <summary>
    /// 注册所有发现的API到路由
    /// </summary>
    private void RegisterExposedApis(IEndpointRouteBuilder endpoints)
    {
        foreach (var api in _exposedApis)
        {
            try
            {
                switch (api.HttpMethod)
                {
                    case "GET":
                        endpoints.MapGet(api.Path, CreateRequestHandler(api));
                        break;
                    case "POST":
                        endpoints.MapPost(api.Path, CreateRequestHandler(api));
                        break;
                    case "PUT":
                        endpoints.MapPut(api.Path, CreateRequestHandler(api));
                        break;
                    case "DELETE":
                        endpoints.MapDelete(api.Path, CreateRequestHandler(api));
                        break;
                    default:
                        _logger.LogWarning($"不支持的HTTP方法: {api.HttpMethod}，接口 {api.Path} 已忽略");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"注册接口 {api.HttpMethod} {api.Path} 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 注册健康检查接口
    /// </summary>
    private void RegisterHealthCheck(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async context =>
        {
            var healthStatus = new
            {
                Status = "Healthy",
                ServerTime = DateTime.UtcNow,
                ExposedApis = _exposedApis.Select(a => $"{a.HttpMethod} {a.Path}"),
                HttpServerRunning = IsRunning,
                DotNetVersion = Environment.Version.ToString()
            };
            await context.Response.WriteAsJsonAsync(healthStatus);
        });
    }

    /// <summary>
    /// 创建请求处理器（动态调用业务方法）
    /// </summary>
    private RequestDelegate CreateRequestHandler(ApiMetadata api)
    {
        return async context =>
        {
            try
            {
                // 获取方法参数
                var parameters = await GetMethodParameters(context, api);

                // 调用业务方法
                var result = await InvokeBusinessMethod(api, parameters);

                // 返回结果
                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object>.SuccessResult(result ?? "操作成功"));
            }
            catch (ArgumentException ex)
            {
                // 处理参数错误，返回400状态码
                _logger.LogError($"接口参数错误 {api.HttpMethod} {api.Path}: {ex.Message}");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object>.ErrorResult(ex.Message, 400));
            }
            catch (Exception ex)
            {
                _logger.LogError($"处理接口 {api.HttpMethod} {api.Path} 出错: {ex.Message}（堆栈：{ex.StackTrace}）");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object>.ErrorResult(ex.Message));
            }
        };
    }

    /// <summary>
    /// 获取方法参数（从请求中解析）
    /// </summary>
    private async Task<object?[]> GetMethodParameters(HttpContext context, ApiMetadata api)
    {
        if (api.ParameterTypes.Length == 0)
            return Array.Empty<object>();

        var jsonOptions = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value;

        // 对于GET请求，从查询参数获取
        if (api.HttpMethod == "GET")
        {
            var parameters = new object?[api.ParameterTypes.Length];
            var query = context.Request.Query;
            var methodParams = api.Method.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramName = methodParams[i].Name;
                if (paramName != null && query.TryGetValue(paramName, out var value))
                {
                    parameters[i] = ConvertParameterValue(value, api.ParameterTypes[i]);
                }
                else if (!methodParams[i].IsOptional)
                {
                    throw new ArgumentException($"缺少必填参数: {paramName}");
                }
            }
            return parameters;
        }
        // 对于其他请求，从请求体获取
        else
        {
            // 验证请求内容类型
            if (!context.Request.ContentType?.Contains("application/json") ?? true)
            {
                throw new ArgumentException("请求内容类型必须为application/json");
            }

            if (api.ParameterTypes.Length == 1)
            {
                var param = await context.Request.ReadFromJsonAsync(api.ParameterTypes[0], jsonOptions.SerializerOptions);
                if (param == null && !api.Method.GetParameters()[0].IsOptional)
                {
                    throw new ArgumentException("请求体不能为null");
                }
                return new[] { param };
            }
            else
            {
                // 多个参数时，从JSON对象解析
                var obj = await context.Request.ReadFromJsonAsync<Dictionary<string, object>>(jsonOptions.SerializerOptions);
                if (obj == null)
                    throw new ArgumentException("请求体不能为null");

                return api.Method.GetParameters()
                    .Select(p =>
                    {
                        if (obj.TryGetValue(p.Name, out var value))
                        {
                            return ConvertParameterValue(value?.ToString(), p.ParameterType);
                        }
                        if (p.IsOptional)
                        {
                            return p.DefaultValue;
                        }
                        throw new ArgumentException($"缺少必填参数: {p.Name}");
                    })
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// 转换参数值到目标类型，增强类型转换的健壮性
    /// </summary>
    private object? ConvertParameterValue(string? value, Type targetType)
    {
        if (value == null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        if (targetType == typeof(string))
            return value;

        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType != null)
        {
            return ConvertParameterValue(value, underlyingType);
        }

        try
        {
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value, ignoreCase: true);

            // 处理常见值类型
            if (targetType == typeof(Guid))
                return Guid.Parse(value);

            if (targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset))
                return DateTimeOffset.Parse(value).UtcDateTime;

            return Convert.ChangeType(value, targetType);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"参数值 '{value}' 无法转换为类型 {targetType.Name}", ex);
        }
    }

    /// <summary>
    /// 调用业务方法
    /// </summary>
    private async Task<object?> InvokeBusinessMethod(ApiMetadata api, object?[] parameters)
    {
        try
        {
            var task = (Task)api.Method.Invoke(_businessWorker, parameters)!;
            await task.ConfigureAwait(false);

            // 获取Task<T>的结果
            if (api.ReturnType.IsGenericType && api.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultProperty = api.ReturnType.GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return null;
        }
        catch (TargetInvocationException ex)
        {
            // 解开反射调用的内部异常
            throw ex.InnerException ?? ex;
        }
    }
}
