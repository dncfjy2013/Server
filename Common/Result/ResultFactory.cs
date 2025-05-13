using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Result
{
    public static class ResultFactory
    {
        // 从异常创建失败结果
        public static Result FromException(Exception exception, string? customMessage = null)
        {
            var errorMessage = customMessage ?? exception.Message;
            return Result.Failure(new[] { errorMessage });
        }

        public static Result<T> FromException<T>(Exception exception, string? customMessage = null)
        {
            var errorMessage = customMessage ?? exception.Message;
            return Result.Failure<T>(new[] { errorMessage });
        }

        // 从条件创建结果
        public static Result Ensure(bool condition, string errorMessage)
        {
            return condition ? Result.Success() : Result.Failure(errorMessage);
        }

        public static Result<T> Ensure<T>(T value, bool condition, string errorMessage)
        {
            return condition ? Result.Success(value) : Result.Failure<T>(errorMessage);
        }

        // 尝试执行方法
        public static Result Try(Action action)
        {
            try
            {
                action();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public static Result<T> Try<T>(Func<T> func)
        {
            try
            {
                return Result.Success(func());
            }
            catch (Exception ex)
            {
                return Result.Failure<T>(ex);
            }
        }

        // 异步尝试执行
        public static async Task<Result> TryAsync(Func<Task> action)
        {
            try
            {
                await action();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex);
            }
        }

        public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func)
        {
            try
            {
                return Result.Success(await func());
            }
            catch (Exception ex)
            {
                return Result.Failure<T>(ex);
            }
        }

        // 并行执行多个异步操作
        public static async Task<Result<IEnumerable<T>>> Parallel<T>(IEnumerable<Task<Result<T>>> tasks)
        {
            var results = await Task.WhenAll(tasks);
            var failedResults = results.Where(r => r.IsFailure).ToList();

            if (failedResults.Any())
            {
                var errors = failedResults.SelectMany(r => r.Errors).ToArray();
                return Result.Failure<IEnumerable<T>>(errors);
            }

            return Result.Success(results.Select(r => r.Value!));
        }
    }
}
