using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace WpfAsyncValidation
{
    public static class ObjectHelper
    {
        public static bool AnyEx<T>(this IEnumerable<T> list)
        {
            if (list == null)
                return false;

            return list.Any();
        }

        public static string GetName(Expression<Func<object>> exp)
        {
            MemberExpression body = exp.Body as MemberExpression;

            if (body == null)
            {
                UnaryExpression ubody = (UnaryExpression)exp.Body;
                body = ubody.Operand as MemberExpression;
            }

            return body.Member.Name;
        }



        public static string GetName<T>(Expression<Func<T, object>> exp)
        {
            MemberExpression body = exp.Body as MemberExpression;

            if (body == null)
            {
                UnaryExpression ubody = (UnaryExpression)exp.Body;
                body = ubody.Operand as MemberExpression;
            }

            return body.Member.Name;
        }

        public static IList<T> ToSingleListEx<T>(this T obj)
        {
            if (obj == null)
                return new List<T>();
            else
                return new T[] { obj }.ToList();
        }
    }
}
