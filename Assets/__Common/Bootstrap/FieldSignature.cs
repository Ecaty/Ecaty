#if UNITY_EDITOR
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Editor-only, allocation-free change detection for authoring assets polled during Play.
    /// Hashes every public serialized float/int/bool/enum/Color field, recursing into [Serializable] class fields.
    /// The per-type reader is compiled once; each call boxes nothing.
    /// </summary>
    public static class FieldSignature
    {
        public static long Of<T>(T value) where T : class => value == null ? 0 : Cache<T>.Read(value);

        static class Cache<T>
        {
            public static readonly Func<T, long> Read = Build();

            static Func<T, long> Build()
            {
                var source = Expression.Parameter(typeof(T), "source");
                var hash = Expression.Variable(typeof(long), "hash");
                var body = new System.Collections.Generic.List<Expression> { Expression.Assign(hash, Expression.Constant(1469598103934665603L)) };
                Append(body, hash, source, typeof(T), 0);
                body.Add(hash);
                return Expression.Lambda<Func<T, long>>(Expression.Block(new[] { hash }, body), source).Compile();
            }
        }

        static void Append(System.Collections.Generic.List<Expression> body, ParameterExpression hash, Expression owner, Type type, int depth)
        {
            if (depth > 4)
                return;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsNotSerialized);
            foreach (var field in fields)
            {
                var value = Expression.Field(owner, field);
                var t = field.FieldType;
                MethodInfo mix = null;
                Expression argument = value;
                if (t == typeof(float))
                    mix = Method(nameof(MixFloat));
                else if (t == typeof(int))
                    mix = Method(nameof(MixInt));
                else if (t == typeof(bool))
                    mix = Method(nameof(MixBool));
                else if (t.IsEnum)
                {
                    mix = Method(nameof(MixInt));
                    argument = Expression.Convert(value, typeof(int));
                }
                else if (t == typeof(Color))
                    mix = Method(nameof(MixColor));
                else if (t.IsClass && t != typeof(string) && t.IsDefined(typeof(SerializableAttribute), false))
                {
                    var nested = Expression.Variable(t);
                    var inner = new System.Collections.Generic.List<Expression>();
                    Append(inner, hash, nested, t, depth + 1);
                    if (inner.Count == 0)
                        continue;
                    body.Add(Expression.Block(new[] { nested },
                        Expression.Assign(nested, value),
                        Expression.IfThen(Expression.NotEqual(nested, Expression.Constant(null, t)), Expression.Block(inner))));
                    continue;
                }
                if (mix != null)
                    body.Add(Expression.Assign(hash, Expression.Call(mix, hash, argument)));
            }
        }

        static MethodInfo Method(string name) => typeof(FieldSignature).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        static long MixInt(long hash, int value) => (hash ^ (uint)value) * 1099511628211L;
        static long MixBool(long hash, bool value) => MixInt(hash, value ? 1 : 0);
        static long MixFloat(long hash, float value) => MixInt(hash, BitConverter.SingleToInt32Bits(value));
        static long MixColor(long hash, Color value) => MixFloat(MixFloat(MixFloat(MixFloat(hash, value.r), value.g), value.b), value.a);
    }
}
#endif
