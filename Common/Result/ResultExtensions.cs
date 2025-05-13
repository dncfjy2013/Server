using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Result
{
    public static class ResultExtensions
    {
        // 转换值类型的扩展方法
        public static Result<TOutput> Map<TInput, TOutput>(this Result<TInput> result, Func<TInput, TOutput> mapFunc)
        {
            if (result.IsFailure)
            {
                return Result.Failure<TOutput>(result.Errors);
            }

            return Result.Success(mapFunc(result.Value!));
        }

        // 链式操作扩展方法
        public static Result<TOutput> Bind<TInput, TOutput>(this Result<TInput> result, Func<TInput, Result<TOutput>> func)
        {
            if (result.IsFailure)
            {
                return Result.Failure<TOutput>(result.Errors);
            }

            return func(result.Value!);
        }

        // 执行副作用操作
        public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
        {
            if (result.IsSuccess)
            {
                action(result.Value!);
            }

            return result;
        }

        // 错误处理
        public static Result<T> OnFailure<T>(this Result<T> result, Action<string[]> action)
        {
            if (result.IsFailure)
            {
                action(result.Errors);
            }

            return result;
        }

        // 合并多个结果
        public static Result Combine(this IEnumerable<Result> results)
        {
            var failedResults = results.Where(r => r.IsFailure).ToList();

            if (!failedResults.Any())
            {
                return Result.Success();
            }

            var errors = failedResults.SelectMany(r => r.Errors).ToArray();
            return Result.Failure(errors);
        }

        // 异步操作支持
        public static async Task<Result<T>> MapAsync<TInput, T>(this Task<Result<TInput>> resultTask, Func<TInput, Task<T>> mapFunc)
        {
            var result = await resultTask;
            if (result.IsFailure)
            {
                return Result.Failure<T>(result.Errors);
            }

            var value = await mapFunc(result.Value!);
            return Result.Success(value);
        }

        // 转换为可选值
        public static Optional<T> ToOptional<T>(this Result<T> result)
        {
            return result.IsSuccess ? Optional.Some(result.Value!) : Optional.None<T>();
        }

        // 获取值或默认值
        public static TValue GetValueOrDefault<TValue>(this Result<TValue> result, TValue defaultValue = default!)
        {
            return result.IsSuccess ? result.Value! : defaultValue;
        }

        // 错误转换
        public static Result<T> MapError<T>(this Result<T> result, Func<string, string> errorMapper)
        {
            if (result.IsSuccess)
            {
                return result;
            }

            var mappedErrors = result.Errors.Select(errorMapper).ToArray();
            return Result.Failure<T>(mappedErrors);
        }

        // 条件执行
        public static Result<T> When<T>(this Result<T> result, bool condition, Func<Result<T>, Result<T>> transform)
        {
            return condition ? transform(result) : result;
        }
    }
}
