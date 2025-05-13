using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Common.Result
{
    // 可选值类型（简化版）
    public readonly struct Optional<T>
    {
        private readonly T _value;
        private readonly bool _hasValue;

        private Optional(T value)
        {
            _value = value;
            _hasValue = true;
        }

        public bool HasValue => _hasValue;
        public T Value => _hasValue ? _value : throw new InvalidOperationException("No value present");

        public static Optional<T> Some(T value) => new Optional<T>(value);
        public static Optional<T> None => new Optional<T>();

        public T GetValueOrDefault(T defaultValue = default!) => _hasValue ? _value : defaultValue;

        public Optional<TResult> Map<TResult>(Func<T, TResult> mapFunc) =>
            _hasValue ? Optional.Some(mapFunc(_value)) : Optional.None<TResult>();

        public Optional<T> Where(Func<T, bool> predicate) =>
            _hasValue && predicate(_value) ? this : Optional.None<T>();

        public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
            _hasValue ? some(_value) : none();

        public void Match(Action<T> some, Action none)
        {
            if (_hasValue)
            {
                some(_value);
            }
            else
            {
                none();
            }
        }
    }

    public static class Optional
    {
        public static Optional<T> Some<T>(T value) => Optional<T>.Some(value);
        public static Optional<T> None<T>() => Optional<T>.None;
    }
}
