using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace Harbour.DBUtils
{
    /// <summary>
    /// 实体转换类（默认有缓存）
    /// </summary>
    public class EntityConverter
    {
        /// <summary>
        /// 将DataRow转为List
        /// </summary>
        /// <typeparam name="T">实体类（必须有默认构造参数）</typeparam>
        /// <param name="dr">DataRow</param>
        /// <returns></returns>
        public static T ToEntity<T>(DataRow dr) where T : new()
        {
            if (dr == null)
                return default(T);

            T t = new T();
            foreach (PropertyInfo prop in typeof(T).GetProperties())
            {
                if (dr.Table.Columns.Contains(prop.Name))
                {
                    if (dr[prop.Name] != DBNull.Value)
                        GetSetter<T>(prop)(t, dr[prop.Name]);
                }
            }
            return t;
        }
        /// <summary>
        /// 将IDataReader转换为实体
        /// </summary>
        /// <typeparam name="T">实体类（必须有默认构造参数）</typeparam>
        /// <param name="dr">IDataReader</param>
        /// <returns></returns>
        public static T ToEntity<T>(IDataReader dr) where T : new()
        {
            T t = default(T);
            if (dr.Read())
            {
                t = new T();
                foreach (PropertyInfo prop in typeof(T).GetProperties())
                {
                    if (dr[prop.Name] != DBNull.Value)
                        GetSetter<T>(prop)(t, dr[prop.Name]);
                }
            }
            return t;
        }

        /// <summary>
        /// 将DataTable转为List
        /// </summary>
        /// <typeparam name="T">实体类（必须有默认构造参数）</typeparam>
        /// <param name="dt">DataTable</param>
        /// <returns></returns>
        public static List<T> ToList<T>(DataTable dt) where T : new()
        {
            List<T> list = new List<T>();
            if (dt == null || dt.Rows.Count == 0)
            {
                return list;
            }

            foreach (DataRow dr in dt.Rows)
            {
                T t = new T();
                foreach (PropertyInfo prop in typeof(T).GetProperties())
                {
                    if (dr.Table.Columns.Contains(prop.Name))
                    {
                        if (dr[prop.Name] != DBNull.Value)
                            GetSetter<T>(prop)(t, dr[prop.Name]);
                    }
                }
                list.Add(t);
            }

            return list;
        }
        /// <summary>
        /// 将IDataReader转为实体
        /// </summary>
        /// <typeparam name="T">实体类（必须有默认构造参数）</typeparam>
        /// <param name="dr">IDataReader</param>
        /// <returns></returns>
        public static List<T> ToList<T>(IDataReader dr) where T : new()
        {
            List<T> list = new List<T>();
            while (dr.Read())
            {
                T t = new T();
                foreach (PropertyInfo prop in typeof(T).GetProperties())
                {
                    if (dr[prop.Name] != DBNull.Value)
                        GetSetter<T>(prop)(t, dr[prop.Name]);
                }
                list.Add(t);
            }
            return list;
        }


        static readonly Dictionary<string, object> _retActDic = new Dictionary<string, object>();
        private static Action<T, object> GetSetter<T>(PropertyInfo property)
        {
            Type type = typeof(T);
            string key = type.AssemblyQualifiedName + "_set_" + property.Name;

            object retAct;
            if (!_retActDic.TryGetValue(key, out retAct))
            {
                lock (key)
                {
                    if (!_retActDic.TryGetValue(key, out retAct))
                    {
                        //创建 对实体 属性赋值的expression
                        ParameterExpression parameter = Expression.Parameter(type, "t");
                        ParameterExpression value = Expression.Parameter(typeof(object), "propertyValue");
                        MethodInfo setter = type.GetMethod("set_" + property.Name);
                        MethodCallExpression call = Expression.Call(parameter, setter, Expression.Convert(value, property.PropertyType));
                        var lambda = Expression.Lambda<Action<T, object>>(call, parameter, value);
                        retAct = lambda.Compile();
                        _retActDic.Add(key, retAct);
                    }
                }
            }
            return retAct as Action<T, object>;
        }

        private static Action<T, object> EmitSetter<T>(string propertyName)
        {
            var type = typeof(T);
            var dynamicMethod = new DynamicMethod("EmitCallable", null, new[] { type, typeof(object) }, type.Module);
            var iLGenerator = dynamicMethod.GetILGenerator();

            var callMethod = type.GetMethod("set_" + propertyName, BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.Public);
            var parameterInfo = callMethod.GetParameters()[0];
            var local = iLGenerator.DeclareLocal(parameterInfo.ParameterType, true);

            iLGenerator.Emit(OpCodes.Ldarg_1);
            if (parameterInfo.ParameterType.IsValueType)
            {
                // 如果是值类型，拆箱
                iLGenerator.Emit(OpCodes.Unbox_Any, parameterInfo.ParameterType);
            }
            else
            {
                // 如果是引用类型，转换
                iLGenerator.Emit(OpCodes.Castclass, parameterInfo.ParameterType);
            }

            iLGenerator.Emit(OpCodes.Stloc, local);
            iLGenerator.Emit(OpCodes.Ldarg_0);
            iLGenerator.Emit(OpCodes.Ldloc, local);

            iLGenerator.EmitCall(OpCodes.Callvirt, callMethod, null);
            iLGenerator.Emit(OpCodes.Ret);

            return dynamicMethod.CreateDelegate(typeof(Action<T, object>)) as Action<T, object>;
        }
    }
}
