namespace AutoExposingServiceFramework.Models;

/// <summary>
/// 通用API响应模型
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public T? Data { get; set; }
    public int Code { get; set; } = 200;

    public static ApiResponse<T> SuccessResult(T data, string message = "操作成功")
        => new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> ErrorResult(string message, int code = 500)
        => new() { Success = false, Message = message, Code = code };
}
