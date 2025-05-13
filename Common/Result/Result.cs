using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Result
{
    public interface IResult
    {
        bool IsSuccess { get; }
        bool IsFailure { get; }
        string[] Errors { get; }
        string ErrorMessage { get; }
        Exception? Exception { get; }
        bool HasException { get; }
    }

    public interface IResult<TValue> : IResult
    {
        TValue? Value { get; }
    }

    public class Result : IResult
    {
        protected internal Result(bool isSuccess, string[] errors, Exception? exception = null)
        {
            if (isSuccess && (errors.Any() || exception is not null))
            {
                throw new InvalidOperationException("成功的结果不能包含错误或异常");
            }

            if (!isSuccess && (!errors.Any() && exception is null))
            {
                throw new InvalidOperationException("失败的结果必须包含至少一个错误或异常");
            }

            IsSuccess = isSuccess;
            Errors = errors;
            Exception = exception;
        }

        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string[] Errors { get; }
        public string ErrorMessage => Exception?.Message ?? string.Join("; ", Errors);
        public Exception? Exception { get; }
        public bool HasException => Exception is not null;

        public static Result Success() => new Result(true, Array.Empty<string>());
        public static Result Failure(string error) => new Result(false, new[] { error });
        public static Result Failure(string[] errors) => new Result(false, errors);
        public static Result Failure(Exception exception) => new Result(false, new[] { exception.Message }, exception);
        public static Result<TValue> Success<TValue>(TValue value) => new Result<TValue>(value, true, Array.Empty<string>());
        public static Result<TValue> Failure<TValue>(string error) => new Result<TValue>(default, false, new[] { error });
        public static Result<TValue> Failure<TValue>(string[] errors) => new Result<TValue>(default, false, errors);
        public static Result<TValue> Failure<TValue>(Exception exception) => new Result<TValue>(default, false, new[] { exception.Message }, exception);
    }

    public class Result<TValue> : Result, IResult<TValue>
    {
        private readonly TValue? _value;

        protected internal Result(TValue? value, bool isSuccess, string[] errors, Exception? exception = null) : base(isSuccess, errors, exception)
        {
            _value = value;
        }

        public TValue? Value
        {
            get
            {
                if (IsFailure)
                {
                    throw new InvalidOperationException("失败的结果没有值");
                }

                return _value;
            }
        }
    }
}
