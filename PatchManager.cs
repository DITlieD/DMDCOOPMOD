using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
namespace DeathMustDieCoop
{
    public static class PatchManager
    {
        private static readonly List<MethodBase> _patchedOriginals = new List<MethodBase>();
        private static readonly Dictionary<IntPtr, DispatchEntry> _dispatchMap =
            new Dictionary<IntPtr, DispatchEntry>();
        public static IEnumerable<MethodBase> PatchedOriginals => _patchedOriginals;
        private class DispatchEntry
        {
            public MethodBase Original;
            public List<MethodInfo> Prefixes = new List<MethodInfo>();
            public List<MethodInfo> Postfixes = new List<MethodInfo>();
        }
        public static void ApplyAll(Assembly assembly)
        {
            int patchCount = 0;
            int skipCount = 0;
            foreach (var type in assembly.GetTypes())
            {
                var attrs = type.GetCustomAttributes(typeof(HarmonyPatch), false);
                if (attrs.Length == 0) continue;
                try
                {
                    var prepare = type.GetMethod("Prepare",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prepare != null)
                    {
                        object result = prepare.Invoke(null, null);
                        if (result is bool b && !b)
                        {
                            CoopPlugin.FileLog($"PatchManager: {type.Name} — Prepare() returned false, skipping.");
                            skipCount++;
                            continue;
                        }
                    }
                    var targets = ResolveTargets(type, attrs);
                    if (targets == null || targets.Count == 0)
                    {
                        CoopPlugin.FileLog($"PatchManager: {type.Name} — no target method resolved, skipping.");
                        skipCount++;
                        continue;
                    }
                    var prefix = type.GetMethod("Prefix",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    var postfix = type.GetMethod("Postfix",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prefix == null && postfix == null)
                    {
                        CoopPlugin.FileLog($"PatchManager: {type.Name} — no Prefix or Postfix found, skipping.");
                        skipCount++;
                        continue;
                    }
                    foreach (var target in targets)
                    {
                        if (target == null) continue;
                        RegisterPatch(target, prefix, postfix, type);
                        patchCount++;
                    }
                }
                catch (Exception ex)
                {
                    CoopPlugin.FileLog($"PatchManager: {type.Name} — ERROR: {ex}");
                }
            }
            int hookCount = 0;
            foreach (var kvp in _dispatchMap)
            {
                var entry = kvp.Value;
                try
                {
                    var hook = GenerateHook(entry);
                    if (hook != null)
                    {
                        NativeDetour.Hook(entry.Original, hook);
                        _patchedOriginals.Add(entry.Original);
                        hookCount++;
                    }
                }
                catch (Exception ex)
                {
                    CoopPlugin.FileLog($"PatchManager: Hook FAILED for {entry.Original.DeclaringType?.Name}.{entry.Original.Name}: {ex}");
                }
            }
            CoopPlugin.FileLog($"PatchManager: Applied {hookCount} hooks from {patchCount} patch methods ({skipCount} skipped).");
        }
        private static List<MethodBase> ResolveTargets(Type patchClass, object[] attrs)
        {
            var targets = new List<MethodBase>();
            var targetMethods = patchClass.GetMethod("TargetMethods",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetMethods != null)
            {
                var result = targetMethods.Invoke(null, null);
                if (result is System.Collections.IEnumerable enumerable)
                {
                    foreach (var m in enumerable)
                    {
                        if (m is MethodBase mb)
                            targets.Add(mb);
                    }
                }
                if (targets.Count > 0) return targets;
            }
            var targetMethod = patchClass.GetMethod("TargetMethod",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (targetMethod != null)
            {
                var result = targetMethod.Invoke(null, null);
                if (result is MethodBase mb)
                {
                    targets.Add(mb);
                    return targets;
                }
            }
            foreach (var attr in attrs)
            {
                if (attr is HarmonyPatch hp)
                {
                    if (hp.DeclaringType != null && !string.IsNullOrEmpty(hp.MethodName))
                    {
                        MethodInfo mi;
                        if (hp.ArgumentTypes != null)
                        {
                            mi = hp.DeclaringType.GetMethod(hp.MethodName,
                                BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.Instance | BindingFlags.Static,
                                null, hp.ArgumentTypes, null);
                        }
                        else
                        {
                            mi = hp.DeclaringType.GetMethod(hp.MethodName,
                                BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.Instance | BindingFlags.Static);
                        }
                        if (mi != null)
                            targets.Add(mi);
                        else
                            CoopPlugin.FileLog($"PatchManager: Could not resolve {hp.DeclaringType.Name}.{hp.MethodName}");
                    }
                }
            }
            return targets;
        }
        private static void RegisterPatch(MethodBase target, MethodInfo prefix, MethodInfo postfix, Type patchClass)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(target.MethodHandle);
            IntPtr addr = target.MethodHandle.GetFunctionPointer();
            if (!_dispatchMap.TryGetValue(addr, out var entry))
            {
                entry = new DispatchEntry { Original = target };
                _dispatchMap[addr] = entry;
            }
            if (prefix != null)
                entry.Prefixes.Add(prefix);
            if (postfix != null)
                entry.Postfixes.Add(postfix);
        }
        private static MethodInfo GenerateHook(DispatchEntry entry)
        {
            return GenerateDispatchMethod(entry);
        }
        private static MethodInfo GenerateDispatchMethod(DispatchEntry entry)
        {
            var original = entry.Original;
            bool isStatic = original.IsStatic;
            var origParams = original.GetParameters();
            var paramTypes = new List<Type>();
            if (!isStatic)
                paramTypes.Add(original.DeclaringType);
            foreach (var p in origParams)
                paramTypes.Add(p.ParameterType);
            Type returnType = (original is MethodInfo mi2) ? mi2.ReturnType : typeof(void);
            var dm = new System.Reflection.Emit.DynamicMethod(
                $"Hook_{original.DeclaringType?.Name}_{original.Name}",
                returnType,
                paramTypes.ToArray(),
                typeof(PatchManager).Module,
                true);
            var il = dm.GetILGenerator();
            var locInstance = il.DeclareLocal(typeof(object));           
            var locArgs = il.DeclareLocal(typeof(object[]));             
            var locResult = il.DeclareLocal(typeof(object));             
            var locSkipOriginal = il.DeclareLocal(typeof(bool));         
            var locDispatchResult = il.DeclareLocal(typeof(object[]));   
            System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(original.MethodHandle);
            long origAddrLong = original.MethodHandle.GetFunctionPointer().ToInt64();
            int paramCount = origParams.Length;
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, paramCount);
            il.Emit(System.Reflection.Emit.OpCodes.Newarr, typeof(object));
            il.Emit(System.Reflection.Emit.OpCodes.Stloc, locArgs);
            int argOffset = isStatic ? 0 : 1;
            for (int i = 0; i < paramCount; i++)
            {
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locArgs);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i);
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg, i + argOffset);
                var pType = origParams[i].ParameterType;
                if (pType.IsByRef)
                {
                    pType = pType.GetElementType();
                    if (pType.IsValueType)
                        il.Emit(System.Reflection.Emit.OpCodes.Ldobj, pType);
                    else
                        il.Emit(System.Reflection.Emit.OpCodes.Ldind_Ref);
                }
                if (pType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Box, pType);
                il.Emit(System.Reflection.Emit.OpCodes.Stelem_Ref);
            }
            if (!isStatic)
            {
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                if (original.DeclaringType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Box, original.DeclaringType);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locInstance);
            }
            else
            {
                il.Emit(System.Reflection.Emit.OpCodes.Ldnull);
                il.Emit(System.Reflection.Emit.OpCodes.Stloc, locInstance);
            }
            il.Emit(System.Reflection.Emit.OpCodes.Ldc_I8, origAddrLong);
            il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locInstance);
            il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locArgs);
            var dispatchMethodInfo = typeof(PatchManager).GetMethod("Dispatch",
                BindingFlags.Public | BindingFlags.Static);
            il.Emit(System.Reflection.Emit.OpCodes.Call, dispatchMethodInfo);
            il.Emit(System.Reflection.Emit.OpCodes.Stloc, locDispatchResult);
            for (int i = 0; i < paramCount; i++)
            {
                if (!origParams[i].ParameterType.IsByRef) continue;
                var elemType = origParams[i].ParameterType.GetElementType();
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg, i + argOffset);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locDispatchResult);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1); 
                il.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, typeof(object[]));
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i);
                il.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);
                if (elemType.IsValueType)
                {
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, elemType);
                    il.Emit(System.Reflection.Emit.OpCodes.Stobj, elemType);
                }
                else
                {
                    il.Emit(System.Reflection.Emit.OpCodes.Castclass, elemType);
                    il.Emit(System.Reflection.Emit.OpCodes.Stind_Ref);
                }
            }
            if (returnType != typeof(void))
            {
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, locDispatchResult);
                il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0); 
                il.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);
                if (returnType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, returnType);
                else
                    il.Emit(System.Reflection.Emit.OpCodes.Castclass, returnType);
            }
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            ForceCompile(dm);
            _keepAlive.Add(dm);
            return dm;
        }
        private static readonly List<object> _keepAlive = new List<object>();
        private static void ForceCompile(System.Reflection.Emit.DynamicMethod dm)
        {
            var createDyn = typeof(System.Reflection.Emit.DynamicMethod).GetMethod(
                "CreateDynMethod", BindingFlags.NonPublic | BindingFlags.Instance);
            if (createDyn != null)
            {
                createDyn.Invoke(dm, null);
                return;
            }
            var getDesc = typeof(System.Reflection.Emit.DynamicMethod).GetMethod(
                "GetMethodDescriptor", BindingFlags.NonPublic | BindingFlags.Instance);
            if (getDesc != null)
            {
                getDesc.Invoke(dm, null);
                return;
            }
            try
            {
                var delType = MakeDelegateType(dm.ReturnType, GetParamTypes(dm));
                if (delType != null)
                {
                    var del = dm.CreateDelegate(delType);
                    _keepAlive.Add(del);
                }
            }
            catch
            {
                CoopPlugin.FileLog($"PatchManager: ForceCompile fallback for {dm.Name}");
            }
        }
        private static Type[] GetParamTypes(System.Reflection.Emit.DynamicMethod dm)
        {
            var parms = dm.GetParameters();
            var types = new Type[parms.Length];
            for (int i = 0; i < parms.Length; i++)
                types[i] = parms[i].ParameterType;
            return types;
        }
        private static Type MakeDelegateType(Type returnType, Type[] paramTypes)
        {
            foreach (var t in paramTypes)
                if (t.IsByRef) return null;
            if (returnType == typeof(void))
            {
                switch (paramTypes.Length)
                {
                    case 0: return typeof(Action);
                    case 1: return typeof(Action<>).MakeGenericType(paramTypes);
                    case 2: return typeof(Action<,>).MakeGenericType(paramTypes);
                    case 3: return typeof(Action<,,>).MakeGenericType(paramTypes);
                    case 4: return typeof(Action<,,,>).MakeGenericType(paramTypes);
                    case 5: return typeof(Action<,,,,>).MakeGenericType(paramTypes);
                    case 6: return typeof(Action<,,,,,>).MakeGenericType(paramTypes);
                    case 7: return typeof(Action<,,,,,,>).MakeGenericType(paramTypes);
                    case 8: return typeof(Action<,,,,,,,>).MakeGenericType(paramTypes);
                    default: return null;
                }
            }
            else
            {
                if (returnType.IsByRef) return null;
                var allTypes = new Type[paramTypes.Length + 1];
                Array.Copy(paramTypes, allTypes, paramTypes.Length);
                allTypes[paramTypes.Length] = returnType;
                switch (paramTypes.Length)
                {
                    case 0: return typeof(Func<>).MakeGenericType(allTypes);
                    case 1: return typeof(Func<,>).MakeGenericType(allTypes);
                    case 2: return typeof(Func<,,>).MakeGenericType(allTypes);
                    case 3: return typeof(Func<,,,>).MakeGenericType(allTypes);
                    case 4: return typeof(Func<,,,,>).MakeGenericType(allTypes);
                    case 5: return typeof(Func<,,,,,>).MakeGenericType(allTypes);
                    case 6: return typeof(Func<,,,,,,>).MakeGenericType(allTypes);
                    case 7: return typeof(Func<,,,,,,,>).MakeGenericType(allTypes);
                    case 8: return typeof(Func<,,,,,,,,>).MakeGenericType(allTypes);
                    default: return null;
                }
            }
        }
        public static object[] Dispatch(long origAddrLong, object instance, object[] args)
        {
            IntPtr addr = new IntPtr(origAddrLong);
            if (!_dispatchMap.TryGetValue(addr, out var entry))
            {
                CoopPlugin.FileLog($"PatchManager: Dispatch called for unknown address 0x{origAddrLong:X}");
                return new object[] { null, args };
            }
            bool skipOriginal = false;
            object result = null;
            bool hasResult = entry.Original is MethodInfo mi && mi.ReturnType != typeof(void);
            foreach (var prefix in entry.Prefixes)
            {
                try
                {
                    object prefixResult = InvokePatch(prefix, entry.Original, instance, args, ref result, hasResult);
                    if (prefix.ReturnType == typeof(bool) && prefixResult is bool b && !b)
                        skipOriginal = true;
                }
                catch (Exception ex)
                {
                    CoopPlugin.FileLog($"PatchManager: Prefix {prefix.DeclaringType?.Name}.{prefix.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            if (!skipOriginal)
            {
                try
                {
                    result = NativeDetour.CallOriginal(entry.Original, instance, args);
                }
                catch (Exception ex)
                {
                    CoopPlugin.FileLog($"PatchManager: Original {entry.Original.DeclaringType?.Name}.{entry.Original.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            foreach (var postfix in entry.Postfixes)
            {
                try
                {
                    InvokePatch(postfix, entry.Original, instance, args, ref result, hasResult);
                }
                catch (Exception ex)
                {
                    CoopPlugin.FileLog($"PatchManager: Postfix {postfix.DeclaringType?.Name}.{postfix.Name} threw: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
            return new object[] { result, args };
        }
        private static object InvokePatch(MethodInfo patch, MethodBase original,
            object instance, object[] args, ref object result, bool hasResult)
        {
            var patchParams = patch.GetParameters();
            var origParams = original.GetParameters();
            var invokeArgs = new object[patchParams.Length];
            int resultParamIndex = -1;
            var refArgIndices = new Dictionary<int, int>(); 
            for (int i = 0; i < patchParams.Length; i++)
            {
                var p = patchParams[i];
                string name = p.Name;
                Type pType = p.ParameterType;
                bool isByRef = pType.IsByRef;
                Type baseType = isByRef ? pType.GetElementType() : pType;
                if (name == "__instance")
                {
                    invokeArgs[i] = instance;
                }
                else if (name == "__result")
                {
                    invokeArgs[i] = result;
                    if (isByRef) resultParamIndex = i;
                }
                else if (name.StartsWith("__") && name.Length > 2 && char.IsDigit(name[2]))
                {
                    int idx = int.Parse(name.Substring(2));
                    if (idx < args.Length)
                    {
                        invokeArgs[i] = args[idx];
                        if (isByRef) refArgIndices[i] = idx;
                    }
                }
                else
                {
                    bool matched = false;
                    for (int j = 0; j < origParams.Length; j++)
                    {
                        if (origParams[j].Name == name)
                        {
                            invokeArgs[i] = j < args.Length ? args[j] : null;
                            if (isByRef) refArgIndices[i] = j;
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        if (baseType.IsValueType)
                            invokeArgs[i] = Activator.CreateInstance(baseType);
                        else
                            invokeArgs[i] = null;
                    }
                }
            }
            object ret = patch.Invoke(null, invokeArgs);
            if (resultParamIndex >= 0)
                result = invokeArgs[resultParamIndex];
            foreach (var kvp in refArgIndices)
            {
                args[kvp.Value] = invokeArgs[kvp.Key];
            }
            return ret;
        }
    }
}