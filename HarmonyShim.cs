using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public Type DeclaringType { get; }
        public string MethodName { get; }
        public Type[] ArgumentTypes { get; }
        public HarmonyPatch() { }
        public HarmonyPatch(Type type)
        {
            DeclaringType = type;
        }
        public HarmonyPatch(Type type, string methodName)
        {
            DeclaringType = type;
            MethodName = methodName;
        }
        public HarmonyPatch(Type type, string methodName, Type[] argumentTypes)
        {
            DeclaringType = type;
            MethodName = methodName;
            ArgumentTypes = argumentTypes;
        }
    }
    public class Harmony
    {
        public string Id { get; }
        public Harmony(string id)
        {
            Id = id;
        }
        public void PatchAll(Assembly assembly) { }
        public IEnumerable<MethodBase> GetPatchedMethods()
        {
            return DeathMustDieCoop.PatchManager.PatchedOriginals;
        }
    }
    public static class AccessTools
    {
        public static Type TypeByName(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }
        public static MethodInfo Method(Type type, string name)
        {
            if (type == null) return null;
            return type.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
        }
        public static MethodInfo Method(Type type, string name, Type[] parameters)
        {
            if (type == null) return null;
            var m = type.GetMethod(name,
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static,
                null, parameters, null);
            if (m != null) return m;
            var candidates = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);
            foreach (var candidate in candidates)
            {
                if (candidate.Name != name) continue;
                var parms = candidate.GetParameters();
                if (parms.Length != parameters.Length) continue;
                bool match = true;
                for (int i = 0; i < parms.Length; i++)
                {
                    var pt = parms[i].ParameterType;
                    if (pt.IsByRef) pt = pt.GetElementType();
                    if (pt != parameters[i]) { match = false; break; }
                }
                if (match) return candidate;
            }
            return null;
        }
    }
    public class Traverse
    {
        private object _root;       
        private Type _type;         
        private MemberInfo _member; 
        private bool _isMethod;
        private MethodInfo _methodInfo;
        private object[] _methodArgs;
        private Traverse() { }
        public static Traverse Create(object instance)
        {
            if (instance == null)
                return new Traverse { _root = null, _type = null };
            return new Traverse
            {
                _root = instance,
                _type = instance.GetType()
            };
        }
        public static Traverse Create(Type type)
        {
            return new Traverse
            {
                _root = null,
                _type = type
            };
        }
        private const BindingFlags ALL_FLAGS =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.FlattenHierarchy;
        public Traverse Field(string name)
        {
            if (_type == null)
                return new Traverse { _root = _root, _type = _type };
            FieldInfo fi = null;
            var t = _type;
            while (t != null && fi == null)
            {
                fi = t.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                t = t.BaseType;
            }
            return new Traverse
            {
                _root = _root,
                _type = fi != null ? fi.FieldType : null,
                _member = fi
            };
        }
        public Traverse Property(string name)
        {
            if (_type == null)
                return new Traverse { _root = _root, _type = _type };
            PropertyInfo pi = null;
            var t = _type;
            while (t != null && pi == null)
            {
                pi = t.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly);
                t = t.BaseType;
            }
            return new Traverse
            {
                _root = _root,
                _type = pi != null ? pi.PropertyType : null,
                _member = pi
            };
        }
        public Traverse Method(string name)
        {
            return MethodInternal(name, null, null);
        }
        public Traverse Method(string name, Type[] paramTypes)
        {
            return MethodInternal(name, paramTypes, null);
        }
        public Traverse Method(string name, object[] arguments)
        {
            Type[] types = null;
            if (arguments != null)
            {
                types = new Type[arguments.Length];
                for (int i = 0; i < arguments.Length; i++)
                    types[i] = arguments[i] != null ? arguments[i].GetType() : typeof(object);
            }
            return MethodInternal(name, types, arguments);
        }
        private Traverse MethodInternal(string name, Type[] paramTypes, object[] args)
        {
            if (_type == null)
                return new Traverse();
            MethodInfo mi = null;
            if (paramTypes != null)
            {
                mi = _type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static,
                    null, paramTypes, null);
                if (mi == null)
                {
                    var t = _type;
                    while (t != null && mi == null)
                    {
                        mi = t.GetMethod(name,
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly,
                            null, paramTypes, null);
                        t = t.BaseType;
                    }
                }
                if (mi == null)
                {
                    var candidates = _type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static);
                    foreach (var c in candidates)
                    {
                        if (c.Name != name) continue;
                        var parms = c.GetParameters();
                        if (parms.Length != paramTypes.Length) continue;
                        bool match = true;
                        for (int i = 0; i < parms.Length; i++)
                        {
                            var pt = parms[i].ParameterType;
                            if (pt.IsByRef) pt = pt.GetElementType();
                            if (!pt.IsAssignableFrom(paramTypes[i])) { match = false; break; }
                        }
                        if (match) { mi = c; break; }
                    }
                }
            }
            else
            {
                mi = _type.GetMethod(name,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static);
                if (mi == null)
                {
                    var t = _type;
                    while (t != null && mi == null)
                    {
                        mi = t.GetMethod(name,
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly);
                        t = t.BaseType;
                    }
                }
            }
            return new Traverse
            {
                _root = _root,
                _type = mi != null ? mi.ReturnType : null,
                _isMethod = true,
                _methodInfo = mi,
                _methodArgs = args
            };
        }
        public T GetValue<T>()
        {
            object val = GetValue();
            if (val == null) return default;
            return (T)val;
        }
        public object GetValue()
        {
            if (_isMethod)
            {
                if (_methodInfo == null) return null;
                return _methodInfo.Invoke(
                    _methodInfo.IsStatic ? null : _root,
                    _methodArgs);
            }
            if (_member is FieldInfo fi)
                return fi.GetValue(fi.IsStatic ? null : _root);
            if (_member is PropertyInfo pi)
            {
                var getter = pi.GetGetMethod(true);
                if (getter == null) return null;
                return getter.Invoke(getter.IsStatic ? null : _root, null);
            }
            return null;
        }
        public object GetValue(params object[] args)
        {
            if (_isMethod && _methodInfo != null)
            {
                return _methodInfo.Invoke(
                    _methodInfo.IsStatic ? null : _root,
                    args);
            }
            return null;
        }
        public Traverse SetValue(object value)
        {
            if (_member is FieldInfo fi)
            {
                fi.SetValue(fi.IsStatic ? null : _root, value);
            }
            else if (_member is PropertyInfo pi)
            {
                var setter = pi.GetSetMethod(true);
                setter?.Invoke(setter.IsStatic ? null : _root, new object[] { value });
            }
            return this;
        }
    }
}