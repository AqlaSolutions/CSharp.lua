/*
Copyright 2016 YANG Huan (sy.yanghuan@gmail.com).
Copyright 2016 Redmoon Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpLua.LuaAst;

namespace CSharpLua {
  public sealed class CmdArgumentException : Exception {
    public CmdArgumentException(string message) : base(message) {
    }
  }

  public sealed class CompilationErrorException : Exception {
    public CompilationErrorException(string message) : base(message) {
    }
  }

  public static class Utility {
    public static T First<T>(this IList<T> list) {
      return list[0];
    }

    public static T Last<T>(this IList<T> list) {
      return list[list.Count - 1];
    }

    public static T GetOrDefault<T>(this IList<T> list, int index, T v = default(T)) {
      return index >= 0 && index < list.Count ? list[index] : v;
    }

    public static T GetOrDefault<K, T>(this IDictionary<K, T> dict, K key, T t = default(T)) {
      T v;
      if (dict.TryGetValue(key, out v)) {
        return v;
      }
      return t;
    }

    public static bool TryAdd<K, V>(this Dictionary<K, HashSet<V>> dict, K key, V value) {
      var set = dict.GetOrDefault(key);
      if (set == null) {
        set = new HashSet<V>();
        dict.Add(key, set);
      }
      return set.Add(value);
    }

    public static void AddAt<T>(this IList<T> list, int index, T v) {
      if (index < list.Count) {
        list[index] = v;
      }
      else {
        int count = index - list.Count;
        for (int i = 0; i < count; ++i) {
          list.Add(default(T));
        }
        list.Add(v);
      }
    }

    public static int IndexOf<T>(this IEnumerable<T> source, Predicate<T> match) {
      int index = 0;
      foreach (var item in source) {
        if (match(item)) {
          return index;
        }
        ++index;
      }
      return -1;
    }

    public static string TrimEnd(this string s, string end) {
      if (s.EndsWith(end)) {
        return s.Remove(s.Length - end.Length, end.Length);
      }
      return s;
    }

    public static Dictionary<string, string[]> GetCommondLines(string[] args) {
      Dictionary<string, string[]> cmds = new Dictionary<string, string[]>();

      string key = "";
      List<string> values = new List<string>();

      foreach (string arg in args) {
        string i = arg.Trim();
        if (i.StartsWith("-")) {
          if (!string.IsNullOrEmpty(key)) {
            cmds.Add(key, values.ToArray());
            key = "";
            values.Clear();
          }
          key = i;
        }
        else {
          values.Add(i);
        }
      }

      if (!string.IsNullOrEmpty(key)) {
        cmds.Add(key, values.ToArray());
      }
      return cmds;
    }

    public static string GetArgument(this Dictionary<string, string[]> args, string name, bool isOption = false) {
      string[] values = args.GetOrDefault(name);
      if (values == null || values.Length == 0) {
        if (isOption) {
          return null;
        }
        throw new CmdArgumentException(name + " is not found");
      }
      return values[0];
    }

    public static string GetCurrentDirectory(string path) {
      const string CurrentDirectorySign1 = "~/";
      const string CurrentDirectorySign2 = "~\\";

      if (path.StartsWith(CurrentDirectorySign1)) {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path.Substring(CurrentDirectorySign1.Length));
      }
      else if (path.StartsWith(CurrentDirectorySign2)) {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path.Substring(CurrentDirectorySign2.Length));
      }

      return Path.Combine(Environment.CurrentDirectory, path);
    }

    public static string[] Split(string s, bool isPath = true) {
      HashSet<string> list = new HashSet<string>();
      if (!string.IsNullOrEmpty(s)) {
        string[] array = s.Split(';');
        foreach (string i in array) {
          list.Add(isPath ? GetCurrentDirectory(i) : i);
        }
      }
      return list.ToArray();
    }

    public static bool IsPrivate(this ISymbol symbol) {
      return symbol.DeclaredAccessibility == Accessibility.Private;
    }

    public static bool IsPrivate(this SyntaxTokenList modifiers) {
      foreach (var modifier in modifiers) {
        switch (modifier.Kind()) {
          case SyntaxKind.PrivateKeyword: {
              return true;
            }
          case SyntaxKind.PublicKeyword:
          case SyntaxKind.InternalKeyword:
          case SyntaxKind.ProtectedKeyword: {
              return false;
            }
        }
      }
      return true;
    }

    public static bool IsStatic(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.StaticKeyword));
    }

    public static bool IsAbstract(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.AbstractKeyword));
    }

    public static bool IsReadOnly(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.ReadOnlyKeyword));
    }

    public static bool IsConst(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.ConstKeyword));
    }

    public static bool IsParams(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.ParamsKeyword));
    }

    public static bool IsPartial(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.PartialKeyword));
    }

    public static bool IsOutOrRef(this SyntaxTokenList modifiers) {
      return modifiers.Any(i => i.IsKind(SyntaxKind.OutKeyword) || i.IsKind(SyntaxKind.RefKeyword));
    }

    public static bool IsStringType(this ITypeSymbol type) {
      return type.SpecialType == SpecialType.System_String;
    }

    public static bool IsDelegateType(this ITypeSymbol type) {
      return type.TypeKind == TypeKind.Delegate;
    }

    public static bool IsIntegerType(this ITypeSymbol type) {
      return type.SpecialType >= SpecialType.System_SByte && type.SpecialType <= SpecialType.System_UInt64;
    }

    public static bool IsNullableType(this ITypeSymbol type) {
      if (type.SpecialType == SpecialType.System_Nullable_T) {
        return true;
      }
      INamedTypeSymbol namedType = type as INamedTypeSymbol;
      return namedType != null && namedType.ConstructedFrom != null && namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;
    }

    public static bool IsImmutable(this ITypeSymbol type) {
      bool isImmutable = (type.IsValueType && type.IsDefinition) || type.IsStringType() || type.IsDelegateType();
      return isImmutable;
    }

    public static bool IsInterfaceImplementation<T>(this T symbol) where T : ISymbol {
      if (!symbol.IsStatic) {
        var type = symbol.ContainingType;
        if (type != null) {
          var interfaceSymbols = type.AllInterfaces.SelectMany(i => i.GetMembers().OfType<T>());
          return interfaceSymbols.Any(i => symbol.Equals(type.FindImplementationForInterfaceMember(i)));
        }
      }
      return false;
    }

    public static IEnumerable<T> InterfaceImplementations<T>(this T symbol) where T : ISymbol {
      if (!symbol.IsStatic) {
        var type = symbol.ContainingType;
        if (type != null) {
          var interfaceSymbols = type.AllInterfaces.SelectMany(i => i.GetMembers().OfType<T>());
          return interfaceSymbols.Where(i => symbol.Equals(type.FindImplementationForInterfaceMember(i)));
        }
      }
      return Array.Empty<T>();
    }

    public static bool IsFromCode(this ISymbol symbol) {
      return !symbol.DeclaringSyntaxReferences.IsEmpty;
    }

    public static bool IsOverridable(this ISymbol symbol) {
      return !symbol.IsStatic && (symbol.IsAbstract || symbol.IsVirtual || symbol.IsOverride);
    }

    public static ISymbol OverriddenSymbol(this ISymbol symbol) {
      switch (symbol.Kind) {
        case SymbolKind.Method: {
            IMethodSymbol methodSymbol = (IMethodSymbol)symbol;
            return methodSymbol.OverriddenMethod;
          }
        case SymbolKind.Property: {
            IPropertySymbol propertySymbol = (IPropertySymbol)symbol;
            return propertySymbol.OverriddenProperty;
          }
        case SymbolKind.Event: {
            IEventSymbol eventSymbol = (IEventSymbol)symbol;
            return eventSymbol.OverriddenEvent;
          }
      }
      return null;
    }

    public static bool IsOverridden(this ISymbol symbol, ISymbol superSymbol) {
      while (true) {
        ISymbol overriddenSymbol = symbol.OverriddenSymbol();
        if (overriddenSymbol != null) {
          CheckOriginalDefinition(ref overriddenSymbol);
          if (overriddenSymbol.Equals(superSymbol)) {
            return true;
          }
          symbol = overriddenSymbol;
        }
        else {
          return false;
        }
      }
    }

    public static bool IsPropertyField(this IPropertySymbol symbol) {
      if (!symbol.IsFromCode() || symbol.IsOverridable()) {
        return false;
      }

      var syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxReference != null) {
        var node = syntaxReference.GetSyntax();
        switch (node.Kind()) {
          case SyntaxKind.PropertyDeclaration: {
              var property = (PropertyDeclarationSyntax)node;
              bool hasGet = false;
              bool hasSet = false;
              if (property.AccessorList != null) {
                foreach (var accessor in property.AccessorList.Accessors) {
                  if (accessor.Body != null) {
                    if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
                      Contract.Assert(!hasGet);
                      hasGet = true;
                    }
                    else {
                      Contract.Assert(!hasSet);
                      hasSet = true;
                    }
                  }
                }
              }
              else {
                Contract.Assert(!hasGet);
                hasGet = true;
              }
              bool isField = !hasGet && !hasSet;
              if (isField) {
                if (symbol.IsInterfaceImplementation()) {
                  isField = false;
                }
              }
              return isField;
            }
          case SyntaxKind.IndexerDeclaration: {
              return false;
            }
          case SyntaxKind.AnonymousObjectMemberDeclarator: {
              return true;
            }
          default: {
              throw new InvalidOperationException();
            }
        }
      }
      return false;
    }
    public static bool IsEventFiled(this IEventSymbol symbol) {
      if (!symbol.IsFromCode() || symbol.IsOverridable()) {
        return false;
      }

      var syntaxReference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxReference != null) {
        bool isField = syntaxReference.GetSyntax().IsKind(SyntaxKind.VariableDeclarator);
        if (isField) {
          if (symbol.IsInterfaceImplementation()) {
            isField = false;
          }
        }
        return isField;
      }
      return false;
    }

    public static bool HasStaticCtor(this INamedTypeSymbol typeSymbol) {
      return typeSymbol.Constructors.Any(i => i.IsStatic);
    }

    public static bool IsStaticLazy(this ISymbol symbol) {
      bool success = symbol.IsStatic && !symbol.IsPrivate();
      if (success) {
        var typeSymbol = symbol.ContainingType;
        return typeSymbol.HasStaticCtor();
      }
      return success;
    }

    public static bool IsAssignment(this SyntaxKind kind) {
      return kind >= SyntaxKind.SimpleAssignmentExpression && kind <= SyntaxKind.RightShiftAssignmentExpression;
    }

    private static INamedTypeSymbol systemLinqEnumerableType_;

    public static bool IsSystemLinqEnumerable(this INamedTypeSymbol symbol) {
      if (systemLinqEnumerableType_ != null) {
        return symbol == systemLinqEnumerableType_;
      }
      else {
        bool success = symbol.ToString() == LuaIdentifierNameSyntax.SystemLinqEnumerable.ValueText;
        if (success) {
          systemLinqEnumerableType_ = symbol;
        }
        return success;
      }
    }

    public static string GetLocationString(this SyntaxNode node) {
      var location = node.SyntaxTree.GetLocation(node.Span);
      var methodInfo = location.GetType().GetMethod("GetDebuggerDisplay", BindingFlags.Instance | BindingFlags.NonPublic);
      return (string)methodInfo.Invoke(location, null);
    }

    public static bool IsSubclassOf(this ITypeSymbol child, ITypeSymbol parent) {
      if (parent.SpecialType == SpecialType.System_Object) {
        return true;
      }

      ITypeSymbol p = child;
      if (p == parent) {
        return false;
      }

      while (p != null) {
        if (p == parent) {
          return true;
        }
        p = p.BaseType;
      }
      return false;
    }

    private static bool IsImplementInterface(this ITypeSymbol implementType, ITypeSymbol interfaceType) {
      ITypeSymbol t = implementType;
      while (t != null) {
        var interfaces = implementType.AllInterfaces;
        foreach (var i in interfaces) {
          if (i == interfaceType || i.IsImplementInterface(interfaceType)) {
            return true;
          }
        }
        t = t.BaseType;
      }
      return false;
    }

    private static bool IsBaseNumberType(this SpecialType specialType) {
      return specialType >= SpecialType.System_Char && specialType <= SpecialType.System_Double;
    }

    private static bool IsNumberTypeAssignableFrom(this ITypeSymbol left, ITypeSymbol right) {
      if (left.SpecialType.IsBaseNumberType() && right.SpecialType.IsBaseNumberType()) {
        SpecialType begin;
        switch (right.SpecialType) {
          case SpecialType.System_Char:
          case SpecialType.System_SByte:
          case SpecialType.System_Byte: {
              begin = SpecialType.System_Int16;
              break;
            }
          case SpecialType.System_Int16:
          case SpecialType.System_UInt16: {
              begin = SpecialType.System_Int32;
              break;
            }
          case SpecialType.System_Int32:
          case SpecialType.System_UInt32: {
              begin = SpecialType.System_Int64;
              break;
            }
          case SpecialType.System_Int64:
          case SpecialType.System_UInt64: {
              begin = SpecialType.System_Decimal;
              break;
            }
          default: {
              begin = SpecialType.System_Double;
              break;
            }
        }
        SpecialType end = SpecialType.System_Double;
        return left.SpecialType >= begin && left.SpecialType <= end;
      }
      return false;
    }

    public static bool IsAssignableFrom(this ITypeSymbol left, ITypeSymbol right) {
      if (left == right) {
        return true;
      }

      if (left.IsNumberTypeAssignableFrom(right)) {
        return true;
      }

      if (right.IsSubclassOf(left)) {
        return true;
      }

      if (left.TypeKind == TypeKind.Interface) {
        return right.IsImplementInterface(left);
      }

      return false;
    }

    private static void CheckSymbolDefinition<T>(ref T symbol) where T : class, ISymbol {
      var originalDefinition = (T)symbol.OriginalDefinition;
      if (originalDefinition != symbol) {
        symbol = originalDefinition;
      }
    }

    public static void CheckMethodDefinition(ref IMethodSymbol symbol) {
      if (symbol.IsExtensionMethod) {
        if (symbol.ReducedFrom != null && symbol.ReducedFrom != symbol) {
          symbol = symbol.ReducedFrom;
        }
      }
      else {
        CheckSymbolDefinition(ref symbol);
      }
    }

    public static void CheckOriginalDefinition(ref ISymbol symbol) {
      if (symbol.Kind == SymbolKind.Method) {
        IMethodSymbol methodSymbol = (IMethodSymbol)symbol;
        CheckMethodDefinition(ref methodSymbol);
        if (methodSymbol != symbol) {
          symbol = methodSymbol;
        }
      }
      else {
        CheckSymbolDefinition(ref symbol);
      }
    }

    public static bool IsMainEntryPoint(this IMethodSymbol symbol) {
      if (symbol.IsStatic && symbol.TypeArguments.IsEmpty && symbol.ContainingType.TypeArguments.IsEmpty && symbol.Name == "Main") {
        if (symbol.ReturnsVoid || symbol.ReturnType.SpecialType == SpecialType.System_Int32) {
          if (symbol.Parameters.IsEmpty) {
            return true;
          }
          else if (symbol.Parameters.Length == 1) {
            var parameterType = symbol.Parameters[0].Type;
            if (parameterType.TypeKind == TypeKind.Array) {
              var arrayType = (IArrayTypeSymbol)parameterType;
              if (arrayType.ElementType.SpecialType == SpecialType.System_String) {
                return true;
              }
            }
          }
        }
      }
      return false;
    }

    public static bool IsExtendSelf(INamedTypeSymbol typeSymbol, INamedTypeSymbol baseTypeSymbol) {
      if (baseTypeSymbol.IsGenericType) {
        foreach (var baseTypeArgument in baseTypeSymbol.TypeArguments) {
          if (baseTypeSymbol.Kind != SymbolKind.TypeParameter) {
            if (!baseTypeArgument.Equals(typeSymbol)) {
              if (typeSymbol.IsAssignableFrom(baseTypeArgument)) {
                return true;
              }
            }
          }
        }
      }
      return false;
    }

    public static bool IsTimeSpanType(this ITypeSymbol typeSymbol) {
      return typeSymbol.ContainingNamespace.Name == "System" && typeSymbol.Name == "TimeSpan";
    }

    public static bool IsGenericIEnumerableType(this ITypeSymbol typeSymbol) {
      return typeSymbol.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    public static bool IsExplicitInterfaceImplementation(this ISymbol symbol) {
      switch (symbol.Kind) {
        case SymbolKind.Property: {
            IPropertySymbol property = (IPropertySymbol)symbol;
            if (property.GetMethod != null) {
              if (property.GetMethod.MethodKind == MethodKind.ExplicitInterfaceImplementation) {
                return true;
              }
              if (property.SetMethod != null) {
                if (property.SetMethod.MethodKind == MethodKind.ExplicitInterfaceImplementation) {
                  return true;
                }
              }
            }
            break;
          }
        case SymbolKind.Method: {
            IMethodSymbol method = (IMethodSymbol)symbol;
            if (method.MethodKind == MethodKind.ExplicitInterfaceImplementation) {
              return true;
            }
            break;
          }
      }
      return false;
    }
  }
}
