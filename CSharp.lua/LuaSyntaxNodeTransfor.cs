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
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using CSharpLua.LuaAst;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;

namespace CSharpLua {
  public sealed partial class LuaSyntaxNodeTransfor : CSharpSyntaxVisitor<LuaSyntaxNode> {
    private sealed class MethodInfo {
      public IMethodSymbol Symbol { get; }
      public IList<LuaExpressionSyntax> RefOrOutParameters { get; }

      public MethodInfo(IMethodSymbol symbol) {
        Symbol = symbol;
        RefOrOutParameters = Array.Empty<LuaExpressionSyntax>();
      }

      public MethodInfo(IMethodSymbol symbol, IList<LuaExpressionSyntax> refOrOutParameters) {
        Symbol = symbol;
        RefOrOutParameters = refOrOutParameters;
      }
    }

    private LuaSyntaxGenerator generator_;
    private SemanticModel semanticModel_;

    private Stack<LuaCompilationUnitSyntax> compilationUnits_ = new Stack<LuaCompilationUnitSyntax>();
    private Stack<LuaTypeDeclarationSyntax> typeDeclarations_ = new Stack<LuaTypeDeclarationSyntax>();
    private Stack<LuaFunctionExpressionSyntax> functions_ = new Stack<LuaFunctionExpressionSyntax>();
    private Stack<MethodInfo> methodInfos_ = new Stack<MethodInfo>();
    private Stack<LuaBlockSyntax> blocks_ = new Stack<LuaBlockSyntax>();
    private Stack<LuaIfStatementSyntax> ifStatements_ = new Stack<LuaIfStatementSyntax>();
    private Stack<LuaSwitchAdapterStatementSyntax> switchs_ = new Stack<LuaSwitchAdapterStatementSyntax>();
    private int baseNameNodeCounter_;

    private static readonly Dictionary<string, string> operatorTokenMapps_ = new Dictionary<string, string>() {
      ["!="] = LuaSyntaxNode.Tokens.NotEquals,
      ["!"] = LuaSyntaxNode.Tokens.Not,
      ["&&"] = LuaSyntaxNode.Tokens.And,
      ["||"] = LuaSyntaxNode.Tokens.Or,
      ["??"] = LuaSyntaxNode.Tokens.Or,
      ["^"] = LuaSyntaxNode.Tokens.BitXor,
    };

    public LuaSyntaxNodeTransfor(LuaSyntaxGenerator generator, SemanticModel semanticModel) {
      generator_ = generator;
      semanticModel_ = semanticModel;
    }

    private XmlMetaProvider XmlMetaProvider {
      get {
        return generator_.XmlMetaProvider;
      }
    }

    private static string GetOperatorToken(SyntaxToken operatorToken) {
      string token = operatorToken.ValueText;
      return operatorTokenMapps_.GetOrDefault(token, token);
    }

    private static string GetOperatorToken(string token) {
      return operatorTokenMapps_.GetOrDefault(token, token);
    }

    private bool IsLuaNewest {
      get {
        return generator_.Setting.IsNewest;
      }
    }

    private LuaCompilationUnitSyntax CurCompilationUnit {
      get {
        return compilationUnits_.Peek();
      }
    }

    private LuaTypeDeclarationSyntax CurType {
      get {
        return typeDeclarations_.Peek();
      }
    }

    private LuaFunctionExpressionSyntax CurFunction {
      get {
        return functions_.Peek();
      }
    }

    private LuaFunctionExpressionSyntax CurFunctionOrNull {
      get {
        return functions_.Count > 0 ? functions_.Peek() : null;
      }
    }

    private MethodInfo CurMethodInfoOrNull {
      get {
        return methodInfos_.Count > 0 ? methodInfos_.Peek() : null;
      }
    }

    private void PushFunction(LuaFunctionExpressionSyntax function) {
      functions_.Push(function);
      ++localMappingCounter_;
    }

    private void PopFunction() {
      functions_.Pop();
      --localMappingCounter_;
      if (localMappingCounter_ == 0) {
        localReservedNames_.Clear();
      }
    }

    private LuaBlockSyntax CurBlock {
      get {
        return blocks_.Peek();
      }
    }

    public override LuaSyntaxNode VisitCompilationUnit(CompilationUnitSyntax node) {
      LuaCompilationUnitSyntax compilationUnit = new LuaCompilationUnitSyntax() { FilePath = node.SyntaxTree.FilePath };
      compilationUnits_.Push(compilationUnit);
      foreach (var member in node.Members) {
        LuaStatementSyntax luaMember = (LuaStatementSyntax)member.Accept(this);
        compilationUnit.AddMember(luaMember);
      }
      compilationUnits_.Pop();
      return compilationUnit;
    }

    public override LuaSyntaxNode VisitNamespaceDeclaration(NamespaceDeclarationSyntax node) {
      var symbol = semanticModel_.GetDeclaredSymbol(node);
      bool isContained = node.Parent.IsKind(SyntaxKind.NamespaceDeclaration);
      LuaIdentifierNameSyntax name = new LuaIdentifierNameSyntax(isContained ? symbol.Name : symbol.ToString());
      LuaNamespaceDeclarationSyntax namespaceDeclaration = new LuaNamespaceDeclarationSyntax(name, isContained);
      foreach (var member in node.Members) {
        var memberNode = (LuaWrapFunctionStatementSynatx)member.Accept(this);
        namespaceDeclaration.AddMemberDeclaration(memberNode);
      }
      return namespaceDeclaration;
    }

    private void BuildTypeMembers(LuaTypeDeclarationSyntax typeDeclaration, TypeDeclarationSyntax node) {
      if (!node.IsKind(SyntaxKind.InterfaceDeclaration)) {
        foreach (var member in node.Members) {
          var newMember = member.Accept(this);
          SyntaxKind kind = member.Kind();
          if (kind >= SyntaxKind.ClassDeclaration && kind <= SyntaxKind.EnumDeclaration) {
            typeDeclaration.AddMemberDeclaration((LuaWrapFunctionStatementSynatx)newMember);
          }
        }
      }
    }

    private void CheckBaseTypeGenericKind(ref bool hasExtendSelf, INamedTypeSymbol typeSymbol, BaseTypeSyntax baseType) {
      if (!hasExtendSelf) {
        if (baseType.IsKind(SyntaxKind.SimpleBaseType)) {
          var baseNode = (SimpleBaseTypeSyntax)baseType;
          if (baseNode.Type.IsKind(SyntaxKind.GenericName)) {
            var baseGenericNameNode = (GenericNameSyntax)baseNode.Type;
            var baseTypeSymbol = (INamedTypeSymbol)semanticModel_.GetTypeInfo(baseGenericNameNode).Type;
            hasExtendSelf = Utility.IsExtendSelf(typeSymbol, baseTypeSymbol);
          }
        }
      }
    }

    private LuaSpeaicalGenericType CheckSpeaicalGenericArgument(INamedTypeSymbol typeSymbol) {
      var interfaceType = typeSymbol.Interfaces.FirstOrDefault(i => i.IsGenericIEnumerableType());
      if (interfaceType != null) {
        bool isBaseImplementation = typeSymbol.BaseType != null && typeSymbol.BaseType.AllInterfaces.Any(i => i.IsGenericIEnumerableType());
        if (!isBaseImplementation) {
          var argumentType = interfaceType.TypeArguments.First();
          var typeName = GetTypeName(argumentType);
          return new LuaSpeaicalGenericType() {
            Name = LuaIdentifierNameSyntax.GenericT,
            Value = typeName,
            IsLazy = argumentType.Kind != SymbolKind.TypeParameter && argumentType.IsFromCode(),
          };
        }
      }
      return null;
    }

    private void BuildTypeDeclaration(INamedTypeSymbol typeSymbol, TypeDeclarationSyntax node, LuaTypeDeclarationSyntax typeDeclaration) {
      typeDeclarations_.Push(typeDeclaration);

      var comments = BuildDocumentationComment(node);
      typeDeclaration.AddDocumentComments(comments);

      var attributes = BuildAttributes(node.AttributeLists);
      typeDeclaration.AddClassAttributes(attributes);

      BuildTypeParameters(typeSymbol, node, typeDeclaration);
      if (node.BaseList != null) {
        bool hasExtendSelf = false;
        List<LuaExpressionSyntax> baseTypes = new List<LuaExpressionSyntax>();
        foreach (var baseType in node.BaseList.Types) {
          var baseTypeName = BuildBaseTypeName(baseType);
          baseTypes.Add(baseTypeName);
          CheckBaseTypeGenericKind(ref hasExtendSelf, typeSymbol, baseType);
        }
        var genericArgument = CheckSpeaicalGenericArgument(typeSymbol);
        typeDeclaration.AddBaseTypes(baseTypes, genericArgument);
        if (hasExtendSelf && !typeSymbol.HasStaticCtor()) {
          typeDeclaration.SetStaticCtorEmpty();
        }
      }

      BuildTypeMembers(typeDeclaration, node);
      if (typeDeclaration.IsNoneCtros) {
        var bseTypeSymbol = typeSymbol.BaseType;
        if (bseTypeSymbol != null) {
          bool isNeedCallBase;
          if (typeDeclaration.IsInitStatementExists) {
            isNeedCallBase = !bseTypeSymbol.Constructors.IsEmpty;
          } else {
            isNeedCallBase = generator_.HasStaticCtor(bseTypeSymbol) || bseTypeSymbol.Constructors.Count() > 1;
          }
          if (isNeedCallBase) {
            var baseCtorInvoke = BuildCallBaseConstructor(typeSymbol);
            if (baseCtorInvoke != null) {
              LuaConstructorAdapterExpressionSyntax function = new LuaConstructorAdapterExpressionSyntax();
              function.AddParameter(LuaIdentifierNameSyntax.This);
              function.AddStatement(baseCtorInvoke);
              typeDeclaration.AddCtor(function, false);
            }
          }
        }
      }

      typeDeclarations_.Pop();
      CurCompilationUnit.AddTypeDeclarationCount();
    }

    private INamedTypeSymbol VisitTypeDeclaration(TypeDeclarationSyntax node, LuaTypeDeclarationSyntax typeDeclaration) {
      INamedTypeSymbol typeSymbol = semanticModel_.GetDeclaredSymbol(node);
      if (node.Modifiers.IsPartial()) {
        if (typeSymbol.DeclaringSyntaxReferences.Length > 1) {
          generator_.AddPartialTypeDeclaration(typeSymbol, node, typeDeclaration, CurCompilationUnit);
          typeDeclaration.IsPartialMark = true;
        } else {
          BuildTypeDeclaration(typeSymbol, node, typeDeclaration);
        }
      } else {
        BuildTypeDeclaration(typeSymbol, node, typeDeclaration);
      }
      return typeSymbol;
    }

    internal void AcceptPartialType(PartialTypeDeclaration major, List<PartialTypeDeclaration> typeDeclarations) {
      compilationUnits_.Push(major.CompilationUnit);
      typeDeclarations_.Push(major.TypeDeclaration);

      List<LuaExpressionSyntax> attributes = new List<LuaExpressionSyntax>();
      foreach (var typeDeclaration in typeDeclarations) {
        semanticModel_ = generator_.GetSemanticModel(typeDeclaration.Node.SyntaxTree);
        var comments = BuildDocumentationComment(typeDeclaration.Node);
        major.TypeDeclaration.AddDocumentComments(comments);

        var expressions = BuildAttributes(typeDeclaration.Node.AttributeLists);
        attributes.AddRange(expressions);
      }
      major.TypeDeclaration.AddClassAttributes(attributes);

      BuildTypeParameters(major.Symbol, major.Node, major.TypeDeclaration);
      List<BaseTypeSyntax> baseTypes = new List<BaseTypeSyntax>();
      HashSet<ITypeSymbol> baseSymbols = new HashSet<ITypeSymbol>();
      foreach (var typeDeclaration in typeDeclarations) {
        if (typeDeclaration.Node.BaseList != null) {
          foreach (var baseTypeSyntax in typeDeclaration.Node.BaseList.Types) {
            var semanticModel = generator_.GetSemanticModel(baseTypeSyntax.SyntaxTree);
            var baseTypeSymbol = semanticModel.GetTypeInfo(baseTypeSyntax.Type).Type;
            if (baseSymbols.Add(baseTypeSymbol)) {
              baseTypes.Add(baseTypeSyntax);
            }
          }
        }
      }

      if (baseTypes.Count > 0) {
        if (baseTypes.Count > 1) {
          var baseTypeIndex = baseTypes.FindIndex(generator_.IsBaseType);
          if (baseTypeIndex > 0) {
            var baseType = baseTypes[baseTypeIndex];
            baseTypes.RemoveAt(baseTypeIndex);
            baseTypes.Insert(0, baseType);
          }
        }

        bool hasExtendSelf = false;
        List<LuaExpressionSyntax> baseTypeExpressions = new List<LuaExpressionSyntax>();
        foreach (var baseType in baseTypes) {
          semanticModel_ = generator_.GetSemanticModel(baseType.SyntaxTree);
          var baseTypeName = BuildBaseTypeName(baseType);
          baseTypeExpressions.Add(baseTypeName);
          CheckBaseTypeGenericKind(ref hasExtendSelf, major.Symbol, baseType);
        }
        var genericArgument = CheckSpeaicalGenericArgument(major.Symbol);
        major.TypeDeclaration.AddBaseTypes(baseTypeExpressions, genericArgument);
        if (hasExtendSelf && !major.Symbol.HasStaticCtor()) {
          major.TypeDeclaration.SetStaticCtorEmpty();
        }
      }

      foreach (var typeDeclaration in typeDeclarations) {
        semanticModel_ = generator_.GetSemanticModel(typeDeclaration.Node.SyntaxTree);
        BuildTypeMembers(major.TypeDeclaration, typeDeclaration.Node);
      }

      typeDeclarations_.Pop();
      compilationUnits_.Pop();

      major.TypeDeclaration.IsPartialMark = false;
      major.CompilationUnit.AddTypeDeclarationCount();
    }

    private LuaIdentifierNameSyntax GetTypeDeclarationName(TypeDeclarationSyntax typeDeclaration) {
      string name = typeDeclaration.Identifier.ValueText;
      if (typeDeclaration.TypeParameterList != null) {
        name += "_" + typeDeclaration.TypeParameterList.Parameters.Count;
      }
      return new LuaIdentifierNameSyntax(name);
    }

    public override LuaSyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
      LuaIdentifierNameSyntax name = GetTypeDeclarationName(node);
      LuaClassDeclarationSyntax classDeclaration = new LuaClassDeclarationSyntax(name);
      VisitTypeDeclaration(node, classDeclaration);
      return classDeclaration;
    }

    public override LuaSyntaxNode VisitStructDeclaration(StructDeclarationSyntax node) {
      LuaIdentifierNameSyntax name = GetTypeDeclarationName(node);
      LuaStructDeclarationSyntax structDeclaration = new LuaStructDeclarationSyntax(name);
      var symbol = VisitTypeDeclaration(node, structDeclaration);
      BuildStructMethods(symbol, structDeclaration);
      generator_.AddTypeSymbol(symbol);
      return structDeclaration;
    }

    public override LuaSyntaxNode VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) {
      LuaIdentifierNameSyntax name = GetTypeDeclarationName(node);
      LuaInterfaceDeclarationSyntax interfaceDeclaration = new LuaInterfaceDeclarationSyntax(name);
      var symbol = VisitTypeDeclaration(node, interfaceDeclaration);
      generator_.AddTypeSymbol(symbol);
      return interfaceDeclaration;
    }

    public override LuaSyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node) {
      INamedTypeSymbol symbol = semanticModel_.GetDeclaredSymbol(node);
      LuaIdentifierNameSyntax name = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      LuaEnumDeclarationSyntax enumDeclaration = new LuaEnumDeclarationSyntax(symbol.ToString(), name, CurCompilationUnit);
      typeDeclarations_.Push(enumDeclaration);
      var comments = BuildDocumentationComment(node);
      enumDeclaration.AddDocumentComments(comments);
      foreach (var member in node.Members) {
        var statement = (LuaKeyValueTableItemSyntax)member.Accept(this);
        enumDeclaration.Add(statement);
      }
      typeDeclarations_.Pop();
      generator_.AddTypeSymbol(symbol);
      generator_.AddEnumDeclaration(enumDeclaration);
      return enumDeclaration;
    }

    private void VisitYield(MethodDeclarationSyntax node, LuaFunctionExpressionSyntax function) {
      Contract.Assert(function.HasYield);

      var nameSyntax = (SimpleNameSyntax)node.ReturnType;
      string name = LuaSyntaxNode.Tokens.Yield + nameSyntax.Identifier.ValueText;
      LuaMemberAccessExpressionSyntax memberAccess = new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.System, new LuaIdentifierNameSyntax(name));
      LuaInvocationExpressionSyntax invokeExpression = new LuaInvocationExpressionSyntax(memberAccess);
      LuaFunctionExpressionSyntax wrapFunction = new LuaFunctionExpressionSyntax();

      var parameters = function.ParameterList.Parameters;
      wrapFunction.ParameterList.Parameters.AddRange(parameters);
      wrapFunction.AddStatements(function.Body.Statements);
      invokeExpression.AddArgument(wrapFunction);
      if (node.ReturnType.IsKind(SyntaxKind.GenericName)) {
        var genericNameSyntax = (GenericNameSyntax)nameSyntax;
        var typeName = genericNameSyntax.TypeArgumentList.Arguments.First();
        var expression = (LuaExpressionSyntax)typeName.Accept(this);
        invokeExpression.AddArgument(expression);
      } else {
        invokeExpression.AddArgument(LuaIdentifierNameSyntax.Object);
      }
      invokeExpression.ArgumentList.Arguments.AddRange(parameters.Select(i => new LuaArgumentSyntax(i.Identifier)));

      LuaReturnStatementSyntax returnStatement = new LuaReturnStatementSyntax(invokeExpression);
      function.Body.Statements.Clear();
      function.AddStatement(returnStatement);
    }

    public override LuaSyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
      if (!node.Modifiers.IsAbstract()) {
        IMethodSymbol symbol = semanticModel_.GetDeclaredSymbol(node);
        List<LuaExpressionSyntax> refOrOutParameters = new List<LuaExpressionSyntax>();
        MethodInfo methodInfo = new MethodInfo(symbol, refOrOutParameters);
        methodInfos_.Push(methodInfo);

        LuaIdentifierNameSyntax methodName = GetMemberName(symbol);
        LuaFunctionExpressionSyntax function = new LuaFunctionExpressionSyntax();
        PushFunction(function);

        var comments = BuildDocumentationComment(node);
        bool isPrivate = symbol.IsPrivate() && symbol.ExplicitInterfaceImplementations.IsEmpty;
        if (!symbol.IsStatic) {
          function.AddParameter(LuaIdentifierNameSyntax.This);
        } else if (symbol.IsMainEntryPoint()) {
          isPrivate = false;
          bool success = generator_.SetMainEntryPoint(symbol);
          if (!success) {
            throw new CompilationErrorException($"{node.GetLocationString()} : has more than one entry point");
          }
        }

        if (!symbol.IsPrivate()) {
          var attributes = BuildAttributes(node.AttributeLists);
          CurType.AddMethodAttributes(methodName, attributes);
        }

        foreach (var parameterNode in node.ParameterList.Parameters) {
          var parameter = (LuaParameterSyntax)parameterNode.Accept(this);
          function.AddParameter(parameter);
          if (parameterNode.Modifiers.IsOutOrRef()) {
            refOrOutParameters.Add(parameter.Identifier);
          }
        }

        if (node.TypeParameterList != null) {
          foreach (var typeParameter in node.TypeParameterList.Parameters) {
            var typeName = (LuaIdentifierNameSyntax)typeParameter.Accept(this);
            function.AddParameter(typeName);
          }
        }

        LuaBlockSyntax block = (LuaBlockSyntax)node.Body.Accept(this);
        function.AddStatements(block.Statements);

        if (function.HasYield) {
          VisitYield(node, function);
        } else {
          if (symbol.ReturnsVoid && refOrOutParameters.Count > 0) {
            var returnStatement = new LuaMultipleReturnStatementSyntax();
            returnStatement.Expressions.AddRange(refOrOutParameters);
            function.AddStatement(returnStatement);
          }
        }
        PopFunction();
        CurType.AddMethod(methodName, function, isPrivate, symbol.IsStaticLazy(), comments);
        methodInfos_.Pop();
        return function;
      }

      return base.VisitMethodDeclaration(node);
    }

    private static LuaExpressionSyntax GetPredefinedDefaultValue(ITypeSymbol typeSymbol) {
      return typeSymbol.IsNullableType() ? LuaIdentifierLiteralExpressionSyntax.Nil : GetPredefinedValueTypeDefaultValue(typeSymbol);
    }

    private static LuaExpressionSyntax GetPredefinedValueTypeDefaultValue(ITypeSymbol typeSymbol) {
      switch (typeSymbol.SpecialType) {
        case SpecialType.None: {
            if (typeSymbol.TypeKind == TypeKind.Enum) {
              return LuaIdentifierLiteralExpressionSyntax.Zero;
            } else if (typeSymbol.IsTimeSpanType()) {
              return BuildDefaultValue(LuaIdentifierNameSyntax.TimeSpan);
            }
            return null;
          }
        case SpecialType.System_Boolean: {
            return new LuaIdentifierLiteralExpressionSyntax(LuaIdentifierNameSyntax.False);
          }
        case SpecialType.System_Char: {
            return new LuaCharacterLiteralExpression(default(char));
          }
        case SpecialType.System_SByte:
        case SpecialType.System_Byte:
        case SpecialType.System_Int16:
        case SpecialType.System_UInt16:
        case SpecialType.System_Int32:
        case SpecialType.System_UInt32:
        case SpecialType.System_Int64:
        case SpecialType.System_UInt64: {
            return LuaIdentifierLiteralExpressionSyntax.Zero;
          }
        case SpecialType.System_Single:
        case SpecialType.System_Double: {
            return LuaIdentifierLiteralExpressionSyntax.ZeroFloat;
          }
        case SpecialType.System_DateTime: {
            return BuildDefaultValue(LuaIdentifierNameSyntax.DateTime);
          }
        default:
          return null;
      }
    }

    private LuaIdentifierNameSyntax GetTempIdentifier(SyntaxNode node) {
      int index = CurFunction.TempIndex++;
      string name = LuaSyntaxNode.TempIdentifiers.GetOrDefault(index);
      if (name == null) {
        throw new CompilationErrorException($"{node.GetLocationString()} : Your code is startling, {LuaSyntaxNode.TempIdentifiers.Length} "
            + "temporary variables is not enough, please refactor your code.");
      }
      return new LuaIdentifierNameSyntax(name);
    }

    private static LuaInvocationExpressionSyntax BuildDefaultValue(LuaExpressionSyntax typeExpression) {
      return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.SystemDefault, typeExpression);
    }

    private LuaInvocationExpressionSyntax BuildDefaultValueExpression(TypeSyntax type) {
      var identifier = (LuaExpressionSyntax)type.Accept(this);
      return BuildDefaultValue(identifier);
    }

    private void VisitBaseFieldDeclarationSyntax(BaseFieldDeclarationSyntax node) {
      if (!node.Modifiers.IsConst()) {
        bool isStatic = node.Modifiers.IsStatic();
        bool isPrivate = node.Modifiers.IsPrivate();
        bool isReadOnly = node.Modifiers.IsReadOnly();

        var type = node.Declaration.Type;
        ITypeSymbol typeSymbol = (ITypeSymbol)semanticModel_.GetSymbolInfo(type).Symbol;
        bool isImmutable = typeSymbol.IsImmutable();
        foreach (var variable in node.Declaration.Variables) {
          var variableSymbol = semanticModel_.GetDeclaredSymbol(variable);
          if (node.IsKind(SyntaxKind.EventFieldDeclaration)) {
            var eventSymbol = (IEventSymbol)variableSymbol;
            if (!IsEventFiled(eventSymbol)) {
              var eventName = GetMemberName(eventSymbol);
              var innerName = AddInnerName(eventSymbol);
              bool valueIsLiteral;
              LuaExpressionSyntax valueExpression = GetFieldValueExpression(type, typeSymbol, variable.Initializer?.Value, out valueIsLiteral);
              CurType.AddEvent(eventName, innerName, valueExpression, isImmutable && valueIsLiteral, isStatic, isPrivate);
              continue;
            }
          } else {
            if (!isStatic && isPrivate) {
              string name = variable.Identifier.ValueText;
              bool success = CheckFieldNameOfProtobufnet(ref name, variableSymbol.ContainingType);
              if (success) {
                AddField(new LuaIdentifierNameSyntax(name), typeSymbol, type, variable.Initializer?.Value, isImmutable, isStatic, isPrivate, isReadOnly, node.AttributeLists);
                continue;
              }
            }
          }
          LuaIdentifierNameSyntax fieldName = GetMemberName(variableSymbol);
          AddField(fieldName, typeSymbol, type, variable.Initializer?.Value, isImmutable, isStatic, isPrivate, isReadOnly, node.AttributeLists);
        }
      } else {
        bool isPrivate = node.Modifiers.IsPrivate();
        var type = node.Declaration.Type;
        ITypeSymbol typeSymbol = (ITypeSymbol)semanticModel_.GetSymbolInfo(type).Symbol;
        if (typeSymbol.SpecialType == SpecialType.System_String) {
          foreach (var variable in node.Declaration.Variables) {
            var value = (LiteralExpressionSyntax)variable.Initializer.Value;
            if (value.Token.ValueText.Length > LuaSyntaxNode.StringConstInlineCount) {
              var variableSymbol = semanticModel_.GetDeclaredSymbol(variable);
              LuaIdentifierNameSyntax fieldName = GetMemberName(variableSymbol);
              AddField(fieldName, typeSymbol, type, value, true, true, isPrivate, true, node.AttributeLists);
            }
          }
        }
      }
    }

    public override LuaSyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node) {
      VisitBaseFieldDeclarationSyntax(node);
      return base.VisitFieldDeclaration(node);
    }

    private LuaExpressionSyntax GetFieldValueExpression(TypeSyntax type, ITypeSymbol typeSymbol, ExpressionSyntax expression, out bool valueIsLiteral) {
      LuaExpressionSyntax valueExpression = null;
      valueIsLiteral = false;
      if (expression != null) {
        valueExpression = (LuaExpressionSyntax)expression.Accept(this);
        valueIsLiteral = expression is LiteralExpressionSyntax;
      }
      if (valueExpression == null) {
        if (typeSymbol.IsValueType && !typeSymbol.IsNullableType()) {
          LuaExpressionSyntax defalutValue = GetPredefinedValueTypeDefaultValue(typeSymbol);
          if (defalutValue != null) {
            valueExpression = defalutValue;
            valueIsLiteral = true;
          } else {
            valueExpression = BuildDefaultValueExpression(type);
          }
        }
      }
      return valueExpression;
    }

    private void AddField(LuaIdentifierNameSyntax name, ITypeSymbol typeSymbol, TypeSyntax type, ExpressionSyntax expression, bool isImmutable, bool isStatic, bool isPrivate, bool isReadOnly, SyntaxList<AttributeListSyntax> attributeLists) {
      if (!(isStatic && isPrivate)) {
        var attributes = BuildAttributes(attributeLists);
        CurType.AddFieldAttributes(name, attributes);
      }
      bool valueIsLiteral;
      LuaExpressionSyntax valueExpression = GetFieldValueExpression(type, typeSymbol, expression, out valueIsLiteral);
      CurType.AddField(name, valueExpression, isImmutable && valueIsLiteral, isStatic, isPrivate, isReadOnly);
    }

    public override LuaSyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
      if (!node.Modifiers.IsAbstract()) {
        var symbol = semanticModel_.GetDeclaredSymbol(node);
        bool isStatic = symbol.IsStatic;
        bool isPrivate = symbol.IsPrivate();
        bool hasGet = false;
        bool hasSet = false;
        if (node.AccessorList != null) {
          LuaIdentifierNameSyntax propertyName = GetMemberName(symbol);
          foreach (var accessor in node.AccessorList.Accessors) {
            if (accessor.Body != null) {
              LuaFunctionExpressionSyntax functionExpression = new LuaFunctionExpressionSyntax();
              if (!isStatic) {
                functionExpression.AddParameter(LuaIdentifierNameSyntax.This);
              }
              PushFunction(functionExpression);
              var block = (LuaBlockSyntax)accessor.Body.Accept(this);
              PopFunction();
              functionExpression.AddStatements(block.Statements);
              LuaPropertyOrEventIdentifierNameSyntax name = new LuaPropertyOrEventIdentifierNameSyntax(true, propertyName);
              CurType.AddMethod(name, functionExpression, isPrivate);
              if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
                Contract.Assert(!hasGet);
                hasGet = true;
              } else {
                Contract.Assert(!hasSet);
                functionExpression.AddParameter(LuaIdentifierNameSyntax.Value);
                name.IsGetOrAdd = false;
                hasSet = true;
              }

              if (!isPrivate) {
                var attributes = BuildAttributes(accessor.AttributeLists);
                CurType.AddMethodAttributes(name, attributes);
              }
            }
          }
        } else {
          Contract.Assert(!hasGet);
          LuaIdentifierNameSyntax propertyName = GetMemberName(symbol);
          LuaPropertyOrEventIdentifierNameSyntax name = new LuaPropertyOrEventIdentifierNameSyntax(true, propertyName);

          LuaFunctionExpressionSyntax functionExpression = new LuaFunctionExpressionSyntax();
          PushFunction(functionExpression);
          blocks_.Push(functionExpression.Body);
          var expression = (LuaExpressionSyntax)node.ExpressionBody.Expression.Accept(this);
          blocks_.Pop();
          PopFunction();

          if (!isStatic) {
            functionExpression.AddParameter(LuaIdentifierNameSyntax.This);
          }
          LuaReturnStatementSyntax returnStatement = new LuaReturnStatementSyntax(expression);
          functionExpression.AddStatement(returnStatement);
          CurType.AddMethod(name, functionExpression, isPrivate);
          hasGet = true;
        }

        if (!hasGet && !hasSet) {
          ITypeSymbol typeSymbol = symbol.Type;
          bool isImmutable = typeSymbol.IsImmutable();
          if (isStatic) {
            LuaIdentifierNameSyntax fieldName = GetMemberName(symbol);
            bool isReadOnly = IsReadOnlyProperty(node);
            AddField(fieldName, typeSymbol, node.Type, node.Initializer?.Value, isImmutable, isStatic, isPrivate, isReadOnly, node.AttributeLists);
          } else {
            bool isField = IsPropertyField(semanticModel_.GetDeclaredSymbol(node));
            if (isField) {
              LuaIdentifierNameSyntax fieldName = GetMemberName(symbol);
              bool isReadOnly = IsReadOnlyProperty(node);
              AddField(fieldName, typeSymbol, node.Type, node.Initializer?.Value, isImmutable, isStatic, isPrivate, isReadOnly, node.AttributeLists);
            } else {
              LuaIdentifierNameSyntax propertyName = GetMemberName(symbol);
              LuaIdentifierNameSyntax innerName = AddInnerName(symbol);
              bool valueIsLiteral;
              LuaExpressionSyntax valueExpression = GetFieldValueExpression(node.Type, typeSymbol, node.Initializer?.Value, out valueIsLiteral);
              CurType.AddProperty(propertyName, innerName, valueExpression, isImmutable && valueIsLiteral, isStatic, isPrivate);
            }
          }
        } else {
          if (!isPrivate) {
            var attributes = BuildAttributes(node.AttributeLists);
            CurType.AddFieldAttributes(new LuaIdentifierNameSyntax(node.Identifier.ValueText), attributes);
          }
        }
      }
      return base.VisitPropertyDeclaration(node);
    }

    private bool IsReadOnlyProperty(PropertyDeclarationSyntax node) {
      return node.AccessorList.Accessors.Count == 1 && node.AccessorList.Accessors[0].Body == null;
    }

    public override LuaSyntaxNode VisitEventDeclaration(EventDeclarationSyntax node) {
      if (!node.Modifiers.IsAbstract()) {
        var symbol = semanticModel_.GetDeclaredSymbol(node);
        bool isStatic = symbol.IsStatic;
        bool isPrivate = symbol.IsPrivate();
        LuaIdentifierNameSyntax eventName = GetMemberName(symbol);
        foreach (var accessor in node.AccessorList.Accessors) {
          LuaFunctionExpressionSyntax functionExpression = new LuaFunctionExpressionSyntax();
          if (!isStatic) {
            functionExpression.AddParameter(LuaIdentifierNameSyntax.This);
          }
          functionExpression.AddParameter(LuaIdentifierNameSyntax.Value);
          PushFunction(functionExpression);
          var block = (LuaBlockSyntax)accessor.Body.Accept(this);
          PopFunction();
          functionExpression.AddStatements(block.Statements);
          LuaPropertyOrEventIdentifierNameSyntax name = new LuaPropertyOrEventIdentifierNameSyntax(false, eventName);
          CurType.AddMethod(name, functionExpression, isPrivate);
          if (accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration)) {
            name.IsGetOrAdd = false;
          }

          if (!isPrivate) {
            var attributes = BuildAttributes(accessor.AttributeLists);
            CurType.AddMethodAttributes(name, attributes);
          }
        }
      }
      return base.VisitEventDeclaration(node);
    }

    public override LuaSyntaxNode VisitEventFieldDeclaration(EventFieldDeclarationSyntax node) {
      VisitBaseFieldDeclarationSyntax(node);
      return base.VisitEventFieldDeclaration(node);
    }

    public override LuaSyntaxNode VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node) {
      IFieldSymbol symbol = semanticModel_.GetDeclaredSymbol(node);
      Contract.Assert(symbol.HasConstantValue);
      LuaIdentifierNameSyntax identifier = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      var value = new LuaIdentifierLiteralExpressionSyntax(symbol.ConstantValue.ToString());
      return new LuaKeyValueTableItemSyntax(new LuaTableLiteralKeySyntax(identifier), value);
    }

    public override LuaSyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node) {
      if (!node.Modifiers.IsAbstract()) {
        var symbol = semanticModel_.GetDeclaredSymbol(node);
        bool isPrivate = symbol.IsPrivate();
        LuaIdentifierNameSyntax indexName = GetMemberName(symbol);
        var parameterList = (LuaParameterListSyntax)node.ParameterList.Accept(this);
        foreach (var accessor in node.AccessorList.Accessors) {
          LuaFunctionExpressionSyntax functionExpression = new LuaFunctionExpressionSyntax();
          functionExpression.AddParameter(LuaIdentifierNameSyntax.This);
          functionExpression.ParameterList.Parameters.AddRange(parameterList.Parameters);
          PushFunction(functionExpression);

          var block = (LuaBlockSyntax)accessor.Body.Accept(this);
          PopFunction();
          functionExpression.AddStatements(block.Statements);
          LuaPropertyOrEventIdentifierNameSyntax name = new LuaPropertyOrEventIdentifierNameSyntax(true, indexName);
          CurType.AddMethod(name, functionExpression, isPrivate);
          if (!accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) {
            functionExpression.AddParameter(LuaIdentifierNameSyntax.Value);
            name.IsGetOrAdd = false;
          }
        }
      }
      return base.VisitIndexerDeclaration(node);
    }

    public override LuaSyntaxNode VisitBracketedParameterList(BracketedParameterListSyntax node) {
      return BuildParameterList(node.Parameters);
    }

    public override LuaSyntaxNode VisitParameterList(ParameterListSyntax node) {
      return BuildParameterList(node.Parameters);
    }

    private LuaSyntaxNode BuildParameterList(SeparatedSyntaxList<ParameterSyntax> parameters) {
      LuaParameterListSyntax parameterList = new LuaParameterListSyntax();
      foreach (var parameter in parameters) {
        var newNode = (LuaParameterSyntax)parameter.Accept(this);
        parameterList.Parameters.Add(newNode);
      }
      return parameterList;
    }

    public override LuaSyntaxNode VisitParameter(ParameterSyntax node) {
      LuaIdentifierNameSyntax identifier = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      LuaParameterSyntax luaParameter = new LuaParameterSyntax(identifier);
      CheckParameterName(ref luaParameter, node);
      return luaParameter;
    }

    private sealed class BlockCommonNode : IComparable<BlockCommonNode> {
      const int kCommentCharCount = 2;
      public SyntaxTrivia Comment { get; }
      public StatementSyntax Statement { get; }
      public FileLinePositionSpan LineSpan { get; }

      public BlockCommonNode(SyntaxTrivia comment) {
        Comment = comment;
        LineSpan = comment.SyntaxTree.GetLineSpan(comment.Span);
      }

      public BlockCommonNode(StatementSyntax statement) {
        Statement = statement;
        LineSpan = statement.SyntaxTree.GetLineSpan(statement.Span);
      }

      public int CompareTo(BlockCommonNode other) {
        return LineSpan.StartLinePosition.CompareTo(other.LineSpan.StartLinePosition);
      }

      public bool Contains(BlockCommonNode other) {
        var otherLineSpan = other.LineSpan;
        return otherLineSpan.StartLinePosition > LineSpan.StartLinePosition
            && otherLineSpan.EndLinePosition < LineSpan.EndLinePosition;
      }

      public void Visit(LuaSyntaxNodeTransfor transfor, LuaBlockSyntax block, ref int lastLine) {
        if (lastLine != -1) {
          int count = LineSpan.StartLinePosition.Line - lastLine - 1;
          if (count > 0) {
            block.Statements.Add(new LuaBlankLinesStatement(count));
          }
        }

        if (Statement != null) {
          LuaStatementSyntax statementNode = (LuaStatementSyntax)Statement.Accept(transfor);
          block.Statements.Add(statementNode);
        } else {
          string content = Comment.ToString();
          if (Comment.IsKind(SyntaxKind.SingleLineCommentTrivia)) {
            string commentContent = content.Substring(kCommentCharCount);
            LuaShortCommentStatement singleComment = new LuaShortCommentStatement(commentContent);
            block.Statements.Add(singleComment);
          } else {
            string commentContent = content.Substring(kCommentCharCount, content.Length - kCommentCharCount - kCommentCharCount);
            LuaLongCommentStatement longComment = new LuaLongCommentStatement(commentContent);
            block.Statements.Add(longComment);
          }
        }

        lastLine = LineSpan.EndLinePosition.Line;
      }
    }

    public override LuaSyntaxNode VisitBlock(BlockSyntax node) {
      LuaBlockStatementSyntax block = new LuaBlockStatementSyntax();
      blocks_.Push(block);

      var comments = node.DescendantTrivia().Where(i => i.IsKind(SyntaxKind.SingleLineCommentTrivia) || i.IsKind(SyntaxKind.MultiLineCommentTrivia));
      var commentNodes = comments.Select(i => new BlockCommonNode(i));

      List<BlockCommonNode> nodes = node.Statements.Select(i => new BlockCommonNode(i)).ToList();
      bool hasComments = false;
      foreach (var comment in commentNodes) {
        bool isContains = nodes.Any(i => i.Contains(comment));
        if (!isContains) {
          nodes.Add(comment);
          hasComments = true;
        }
      }
      if (hasComments) {
        nodes.Sort();
      }

      int lastLine = -1;
      foreach (var common in nodes) {
        common.Visit(this, block, ref lastLine);
      }

      blocks_.Pop();
      return block;
    }

    public override LuaSyntaxNode VisitReturnStatement(ReturnStatementSyntax node) {
      if (CurFunction is LuaCheckReturnFunctionExpressionSyntax) {
        LuaMultipleReturnStatementSyntax returnStatement = new LuaMultipleReturnStatementSyntax();
        returnStatement.Expressions.Add(LuaIdentifierNameSyntax.True);
        if (node.Expression != null) {
          var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
          returnStatement.Expressions.Add(expression);
        }
        return returnStatement;
      } else {
        var curMethodInfo = CurMethodInfoOrNull;
        if (curMethodInfo != null && curMethodInfo.RefOrOutParameters.Count > 0) {
          LuaMultipleReturnStatementSyntax returnStatement = new LuaMultipleReturnStatementSyntax();
          if (node.Expression != null) {
            var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
            returnStatement.Expressions.Add(expression);
          }
          returnStatement.Expressions.AddRange(curMethodInfo.RefOrOutParameters);
          return returnStatement;
        } else {
          var expression = (LuaExpressionSyntax)node.Expression?.Accept(this);
          return new LuaReturnStatementSyntax(expression);
        }
      }
    }

    public override LuaSyntaxNode VisitExpressionStatement(ExpressionStatementSyntax node) {
      LuaExpressionSyntax expressionNode = (LuaExpressionSyntax)node.Expression.Accept(this);
      if (expressionNode != LuaExpressionSyntax.EmptyExpression) {
        return new LuaExpressionStatementSyntax(expressionNode);
      } else {
        return LuaStatementSyntax.Empty;
      }
    }

    private LuaExpressionSyntax BuildCommonAssignmentExpression(LuaExpressionSyntax left, LuaExpressionSyntax right, string operatorToken, bool isRightParenthesized) {
      var propertyAdapter = left as LuaPropertyAdapterExpressionSyntax;
      if (propertyAdapter != null) {
        propertyAdapter.ArgumentList.AddArgument(new LuaBinaryExpressionSyntax(propertyAdapter.GetCloneOfGet(), operatorToken, right));
        return propertyAdapter;
      } else {
        if (isRightParenthesized) {
          right = new LuaParenthesizedExpressionSyntax(right);
        }
        return new LuaAssignmentExpressionSyntax(left, new LuaBinaryExpressionSyntax(left, operatorToken, right));
      }
    }

    private LuaExpressionSyntax BuildCommonAssignmentExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode, string operatorToken) {
      var left = (LuaExpressionSyntax)leftNode.Accept(this);
      var right = (LuaExpressionSyntax)rightNode.Accept(this);
      return BuildCommonAssignmentExpression(left, right, operatorToken, rightNode is BinaryExpressionSyntax);
    }

    private LuaExpressionSyntax BuildDelegateAssignmentExpression(LuaExpressionSyntax left, LuaExpressionSyntax right, bool isPlus) {
      var methodName = isPlus ? LuaIdentifierNameSyntax.DelegateCombine : LuaIdentifierNameSyntax.DelegateRemove;
      var propertyAdapter = left as LuaPropertyAdapterExpressionSyntax;
      if (propertyAdapter != null) {
        if (propertyAdapter.IsProperty) {
          propertyAdapter.ArgumentList.AddArgument(new LuaInvocationExpressionSyntax(methodName, propertyAdapter.GetCloneOfGet(), right));
          return propertyAdapter;
        } else {
          propertyAdapter.IsGetOrAdd = isPlus;
          propertyAdapter.ArgumentList.AddArgument(right);
          return propertyAdapter;
        }
      } else {
        return new LuaAssignmentExpressionSyntax(left, new LuaInvocationExpressionSyntax(methodName, left, right));
      }
    }

    private LuaExpressionSyntax BuildBinaryInvokeAssignmentExpression(LuaExpressionSyntax left, LuaExpressionSyntax right, LuaIdentifierNameSyntax methodName) {
      var propertyAdapter = left as LuaPropertyAdapterExpressionSyntax;
      if (propertyAdapter != null) {
        LuaInvocationExpressionSyntax invocation = new LuaInvocationExpressionSyntax(methodName, propertyAdapter.GetCloneOfGet(), right);
        propertyAdapter.ArgumentList.AddArgument(invocation);
        return propertyAdapter;
      } else {
        LuaInvocationExpressionSyntax invocation = new LuaInvocationExpressionSyntax(methodName, left, right);
        return new LuaAssignmentExpressionSyntax(left, invocation);
      }
    }

    private LuaExpressionSyntax BuildBinaryInvokeAssignmentExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode, LuaIdentifierNameSyntax methodName) {
      var left = (LuaExpressionSyntax)leftNode.Accept(this);
      var right = (LuaExpressionSyntax)rightNode.Accept(this);
      return BuildBinaryInvokeAssignmentExpression(left, right, methodName);
    }

    private LuaExpressionSyntax BuildIntegerDivAssignmentExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode) {
      if (IsLuaNewest) {
        return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.IntegerDiv);
      } else {
        return BuildBinaryInvokeAssignmentExpression(leftNode, rightNode, LuaIdentifierNameSyntax.IntegerDiv);
      }
    }

    private LuaExpressionSyntax BuildLuaSimpleAssignmentExpression(LuaExpressionSyntax left, LuaExpressionSyntax right) {
      var propertyAdapter = left as LuaPropertyAdapterExpressionSyntax;
      if (propertyAdapter != null) {
        propertyAdapter.IsGetOrAdd = false;
        propertyAdapter.ArgumentList.AddArgument(right);
        return propertyAdapter;
      } else {
        return new LuaAssignmentExpressionSyntax(left, right);
      }
    }

    private LuaExpressionSyntax BuildLuaAssignmentExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode, SyntaxKind kind) {
      switch (kind) {
        case SyntaxKind.SimpleAssignmentExpression: {
            var left = (LuaExpressionSyntax)leftNode.Accept(this);
            var right = (LuaExpressionSyntax)rightNode.Accept(this);
            CheckValueTypeAndConversion(rightNode, ref right);
            return BuildLuaSimpleAssignmentExpression(left, right);
          }
        case SyntaxKind.AddAssignmentExpression: {
            var leftType = semanticModel_.GetTypeInfo(leftNode).Type;
            if (leftType.IsStringType()) {
              var left = (LuaExpressionSyntax)leftNode.Accept(this);
              var right = WrapStringConcatExpression(rightNode);
              return BuildCommonAssignmentExpression(left, right, LuaSyntaxNode.Tokens.Concatenation, rightNode is BinaryExpressionSyntax);
            } else {
              var left = (LuaExpressionSyntax)leftNode.Accept(this);
              var right = (LuaExpressionSyntax)rightNode.Accept(this);

              if (leftType.IsDelegateType()) {
                return BuildDelegateAssignmentExpression(left, right, true);
              } else {
                return BuildCommonAssignmentExpression(left, right, LuaSyntaxNode.Tokens.Plus, rightNode is BinaryExpressionSyntax);
              }
            }
          }
        case SyntaxKind.SubtractAssignmentExpression: {
            var left = (LuaExpressionSyntax)leftNode.Accept(this);
            var right = (LuaExpressionSyntax)rightNode.Accept(this);

            var leftType = semanticModel_.GetTypeInfo(leftNode).Type;
            if (leftType.IsDelegateType()) {
              return BuildDelegateAssignmentExpression(left, right, false);
            } else {
              return BuildCommonAssignmentExpression(left, right, LuaSyntaxNode.Tokens.Sub, rightNode is BinaryExpressionSyntax);
            }
          }
        case SyntaxKind.MultiplyAssignmentExpression: {
            return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.Multiply);
          }
        case SyntaxKind.DivideAssignmentExpression: {
            var leftType = semanticModel_.GetTypeInfo(leftNode).Type;
            var rightType = semanticModel_.GetTypeInfo(rightNode).Type;
            if (leftType.IsIntegerType() && rightType.IsIntegerType()) {
              return BuildIntegerDivAssignmentExpression(leftNode, rightNode);
            } else {
              return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.Div);
            }
          }
        case SyntaxKind.ModuloAssignmentExpression: {
            if (!IsLuaNewest) {
              var leftType = semanticModel_.GetTypeInfo(leftNode).Type;
              var rightType = semanticModel_.GetTypeInfo(rightNode).Type;
              if (leftType.IsIntegerType() && rightType.IsIntegerType()) {
                return BuildBinaryInvokeAssignmentExpression(leftNode, rightNode, LuaIdentifierNameSyntax.IntegerMod);
              }
            }
            return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.Mod);
          }
        case SyntaxKind.AndAssignmentExpression: {
            return BuildBitAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.And, LuaSyntaxNode.Tokens.BitAnd, LuaIdentifierNameSyntax.BitAnd);
          }
        case SyntaxKind.ExclusiveOrAssignmentExpression: {
            return BuildBitAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.NotEquals, LuaSyntaxNode.Tokens.BitXor, LuaIdentifierNameSyntax.BitXor);
          }
        case SyntaxKind.OrAssignmentExpression: {
            return BuildBitAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.Or, LuaSyntaxNode.Tokens.BitOr, LuaIdentifierNameSyntax.BitOr);
          }
        case SyntaxKind.LeftShiftAssignmentExpression: {
            if (IsLuaNewest) {
              return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.LeftShift);
            } else {
              return BuildBinaryInvokeAssignmentExpression(leftNode, rightNode, LuaIdentifierNameSyntax.ShiftLeft);
            }
          }
        case SyntaxKind.RightShiftAssignmentExpression: {
            if (IsLuaNewest) {
              return BuildCommonAssignmentExpression(leftNode, rightNode, LuaSyntaxNode.Tokens.RightShift);
            } else {
              return BuildBinaryInvokeAssignmentExpression(leftNode, rightNode, LuaIdentifierNameSyntax.ShiftRight);
            }
          }
        default:
          throw new NotImplementedException();
      }
    }

    private LuaExpressionSyntax BuildBitAssignmentExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode, string boolOperatorToken, string otherOperatorToken, LuaIdentifierNameSyntax methodName) {
      var leftType = semanticModel_.GetTypeInfo(leftNode).Type;
      if (leftType.SpecialType == SpecialType.System_Boolean) {
        return BuildCommonAssignmentExpression(leftNode, rightNode, boolOperatorToken);
      } else if (!IsLuaNewest) {
        return BuildBinaryInvokeAssignmentExpression(leftNode, rightNode, methodName);
      } else {
        string operatorToken = GetOperatorToken(otherOperatorToken);
        return BuildCommonAssignmentExpression(leftNode, rightNode, operatorToken);
      }
    }

    public override LuaSyntaxNode VisitAssignmentExpression(AssignmentExpressionSyntax node) {
      List<LuaExpressionSyntax> assignments = new List<LuaExpressionSyntax>();

      while (true) {
        var leftExpression = node.Left;
        var rightExpression = node.Right;
        var kind = node.Kind();

        var assignmentRight = rightExpression as AssignmentExpressionSyntax;
        if (assignmentRight == null) {
          assignments.Add(BuildLuaAssignmentExpression(leftExpression, rightExpression, kind));
          break;
        } else {
          assignments.Add(BuildLuaAssignmentExpression(leftExpression, assignmentRight.Left, kind));
          node = assignmentRight;
        }
      }

      if (assignments.Count == 1) {
        return assignments.First();
      } else {
        assignments.Reverse();
        LuaLineMultipleExpressionSyntax multipleAssignment = new LuaLineMultipleExpressionSyntax();
        multipleAssignment.Assignments.AddRange(assignments);
        return multipleAssignment;
      }
    }

    private LuaExpressionSyntax BuildInvokeRefOrOut(InvocationExpressionSyntax node, LuaExpressionSyntax invocation, IEnumerable<LuaExpressionSyntax> refOrOutArguments) {
      if (node.Parent.IsKind(SyntaxKind.ExpressionStatement)) {
        LuaMultipleAssignmentExpressionSyntax multipleAssignment = new LuaMultipleAssignmentExpressionSyntax();
        SymbolInfo symbolInfo = semanticModel_.GetSymbolInfo(node);
        IMethodSymbol symbol = (IMethodSymbol)symbolInfo.Symbol;
        if (!symbol.ReturnsVoid) {
          var temp = GetTempIdentifier(node);
          CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(new LuaVariableDeclaratorSyntax(temp)));
          multipleAssignment.Lefts.Add(temp);
        }
        multipleAssignment.Lefts.AddRange(refOrOutArguments);
        multipleAssignment.Rights.Add(invocation);
        return multipleAssignment;
      } else {
        var temp = GetTempIdentifier(node);
        LuaMultipleAssignmentExpressionSyntax multipleAssignment = new LuaMultipleAssignmentExpressionSyntax();
        multipleAssignment.Lefts.Add(temp);
        multipleAssignment.Lefts.AddRange(refOrOutArguments);
        multipleAssignment.Rights.Add(invocation);

        CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(new LuaVariableDeclaratorSyntax(temp)));
        CurBlock.Statements.Add(new LuaExpressionStatementSyntax(multipleAssignment));
        return temp;
      }
    }

    private LuaExpressionSyntax CheckCodeTemplateInvocationExpression(IMethodSymbol symbol, InvocationExpressionSyntax node) {
      if (node.Expression.IsKind(SyntaxKind.SimpleMemberAccessExpression)) {
        string codeTemplate = XmlMetaProvider.GetMethodCodeTemplate(symbol);
        if (codeTemplate != null) {
          List<ExpressionSyntax> argumentExpressions = new List<ExpressionSyntax>();
          var memberAccessExpression = (MemberAccessExpressionSyntax)node.Expression;
          if (symbol.IsExtensionMethod) {
            argumentExpressions.Add(memberAccessExpression.Expression);
            if (symbol.ContainingType.IsSystemLinqEnumerable()) {
              CurCompilationUnit.ImportLinq();
            }
          }
          argumentExpressions.AddRange(node.ArgumentList.Arguments.Select(i => i.Expression));
          var invocationExpression = BuildCodeTemplateExpression(codeTemplate, memberAccessExpression.Expression, argumentExpressions, symbol.TypeArguments);
          var refOrOuts = node.ArgumentList.Arguments.Where(i => i.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) || i.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword));
          if (refOrOuts.Any()) {
            return BuildInvokeRefOrOut(node, invocationExpression, refOrOuts.Select(i => ((LuaArgumentSyntax)i.Accept(this)).Expression));
          } else {
            return invocationExpression;
          }
        }
      }
      return null;
    }

    public override LuaSyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
      var constExpression = GetConstExpression(node);
      if (constExpression != null) {
        return constExpression;
      }

      var symbol = (IMethodSymbol)semanticModel_.GetSymbolInfo(node).Symbol;
      if (symbol != null) {
        var codeTemplateExpression = CheckCodeTemplateInvocationExpression(symbol, node);
        if (codeTemplateExpression != null) {
          return codeTemplateExpression;
        }
      }

      List<LuaExpressionSyntax> refOrOutArguments = new List<LuaExpressionSyntax>();
      List<LuaExpressionSyntax> arguments;
      if (symbol != null) {
        arguments = BuildArgumentList(symbol, symbol.Parameters, node.ArgumentList, refOrOutArguments);
        bool ignoreGeneric = generator_.XmlMetaProvider.IsMethodIgnoreGeneric(symbol);
        if (!ignoreGeneric) {
          foreach (var typeArgument in symbol.TypeArguments) {
            LuaExpressionSyntax typeName = GetTypeName(typeArgument);
            arguments.Add(typeName);
          }
        }
        TryRemoveNilArgumentsAtTail(symbol, arguments);
      } else {
        arguments = new List<LuaExpressionSyntax>();
        foreach (var argument in node.ArgumentList.Arguments) {
          if (argument.NameColon != null) {
            throw new CompilationErrorException($"{argument.GetLocationString()} : named argument is not support.");
          }
          FillInvocationArgument(arguments, argument, ImmutableArray<IParameterSymbol>.Empty, refOrOutArguments);
        }
      }

      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      LuaInvocationExpressionSyntax invocation;
      if (symbol != null && symbol.IsExtensionMethod) {
        LuaMemberAccessExpressionSyntax memberAccess = expression as LuaMemberAccessExpressionSyntax;
        if (memberAccess != null) {
          if (memberAccess.Name is LuaInternalMethodExpressionSyntax) {
            invocation = new LuaInvocationExpressionSyntax(memberAccess.Name);
            invocation.AddArgument(memberAccess.Expression);
          } else {
            invocation = BuildExtensionMethodInvocation(symbol.ReducedFrom, memberAccess.Expression, node);
          }
        } else {
          invocation = new LuaInvocationExpressionSyntax(expression);
        }
      } else {
        LuaMemberAccessExpressionSyntax memberAccess = expression as LuaMemberAccessExpressionSyntax;
        if (memberAccess != null) {
          if (memberAccess.Name is LuaInternalMethodExpressionSyntax) {
            invocation = new LuaInvocationExpressionSyntax(memberAccess.Name);
            invocation.AddArgument(memberAccess.Expression);
          } else {
            invocation = new LuaInvocationExpressionSyntax(memberAccess);
          }
        } else {
          invocation = new LuaInvocationExpressionSyntax(expression);
          if (expression is LuaInternalMethodExpressionSyntax) {
            if (!symbol.IsStatic) {
              invocation.AddArgument(LuaIdentifierNameSyntax.This);
            }
          }
        }
      }

      invocation.AddArguments(arguments);
      if (refOrOutArguments.Count > 0) {
        return BuildInvokeRefOrOut(node, invocation, refOrOutArguments);
      } else {
        return invocation;
      }
    }

    private LuaInvocationExpressionSyntax BuildExtensionMethodInvocation(IMethodSymbol reducedFrom, LuaExpressionSyntax expression, InvocationExpressionSyntax node) {
      LuaExpressionSyntax typeName = GetTypeName(reducedFrom.ContainingType);
      LuaIdentifierNameSyntax methodName = GetMemberName(reducedFrom);
      LuaMemberAccessExpressionSyntax typeMemberAccess = new LuaMemberAccessExpressionSyntax(typeName, methodName);
      LuaInvocationExpressionSyntax invocation = new LuaInvocationExpressionSyntax(typeMemberAccess);
      invocation.AddArgument(expression);
      return invocation;
    }

    private LuaExpressionSyntax GetDeafultParameterValue(IParameterSymbol parameter, SyntaxNode node, bool isCheckCallerAttribute) {
      Contract.Assert(parameter.HasExplicitDefaultValue);
      LuaExpressionSyntax defaultValue = isCheckCallerAttribute ? CheckCallerAttribute(parameter, node) : null;
      if (defaultValue == null) {
        if (parameter.ExplicitDefaultValue == null && parameter.Type.IsValueType) {
          defaultValue = GetPredefinedDefaultValue(parameter.Type);
          if (defaultValue == null) {
            defaultValue = BuildDefaultValue(GetTypeName(parameter.Type));
          }
        } else {
          defaultValue = GetLiteralExpression(parameter.ExplicitDefaultValue);
        }
      }
      Contract.Assert(defaultValue != null);
      return defaultValue;
    }

    private void CheckInvocationDeafultArguments(ISymbol symbol, ImmutableArray<IParameterSymbol> parameters, List<LuaExpressionSyntax> arguments, List<Tuple<NameColonSyntax, ExpressionSyntax>> argumentNodeInfos, SyntaxNode node, bool isCheckCallerAttribute) {
      if (parameters.Length > arguments.Count) {
        var optionalParameters = parameters.Skip(arguments.Count);
        foreach (IParameterSymbol parameter in optionalParameters) {
          if (parameter.IsParams) {
            var arrayType = (IArrayTypeSymbol)parameter.Type;
            LuaExpressionSyntax baseType = GetTypeName(arrayType.ElementType);
            LuaExpressionSyntax emptyArray = BuildEmptyArray(baseType);
            arguments.Add(emptyArray);
          } else {
            LuaExpressionSyntax defaultValue = GetDeafultParameterValue(parameter, node, isCheckCallerAttribute);
            arguments.Add(defaultValue);
          }
        }
      } else if (!parameters.IsEmpty) {
        IParameterSymbol last = parameters.Last();
        if (last.IsParams && symbol.IsFromCode()) {
          if (parameters.Length == arguments.Count) {
            var paramsArgument = argumentNodeInfos.Last();
            if (paramsArgument.Item1 != null) {
              string name = paramsArgument.Item1.Name.Identifier.ValueText;
              if (name != last.Name) {
                paramsArgument = argumentNodeInfos.First(i => i.Item1 != null && i.Item1.Name.Identifier.ValueText == last.Name);
              }
            }
            var paramsType = semanticModel_.GetTypeInfo(paramsArgument.Item2).Type;
            if (paramsType.TypeKind != TypeKind.Array) {
              var arrayTypeSymbol = (IArrayTypeSymbol)last.Type;
              var array = BuildArray(arrayTypeSymbol.ElementType, arguments.Last());
              arguments[arguments.Count - 1] = array;
            }
          } else {
            int otherParameterCount = parameters.Length - 1;
            var arrayTypeSymbol = (IArrayTypeSymbol)last.Type;
            var paramsArguments = arguments.Skip(otherParameterCount);
            var array = BuildArray(arrayTypeSymbol.ElementType, paramsArguments);
            arguments.RemoveRange(otherParameterCount, arguments.Count - otherParameterCount);
            arguments.Add(array);
          }
        }
      }

      for (int i = 0; i < arguments.Count; ++i) {
        if (arguments[i] == null) {
          LuaExpressionSyntax defaultValue = GetDeafultParameterValue(parameters[i], node, isCheckCallerAttribute);
          arguments[i] = defaultValue;
        }
      }
    }

    private void CheckInvocationDeafultArguments(ISymbol symbol, ImmutableArray<IParameterSymbol> parameters, List<LuaExpressionSyntax> arguments, BaseArgumentListSyntax node) {
      var argumentNodeInfos = node.Arguments.Select(i => Tuple.Create(i.NameColon, i.Expression)).ToList();
      CheckInvocationDeafultArguments(symbol, parameters, arguments, argumentNodeInfos, node.Parent, true);
    }

    private LuaExpressionSyntax BuildMemberAccessTargetExpression(ExpressionSyntax targetExpression) {
      var expression = (LuaExpressionSyntax)targetExpression.Accept(this);
      SyntaxKind kind = targetExpression.Kind();
      if ((kind >= SyntaxKind.NumericLiteralExpression && kind <= SyntaxKind.NullLiteralExpression)
          || (expression is LuaLiteralExpressionSyntax)) {
        expression = new LuaParenthesizedExpressionSyntax(expression);
      }
      return expression;
    }

    private LuaExpressionSyntax BuildMemberAccessExpression(ISymbol symbol, ExpressionSyntax node) {
      bool isExtensionMethod = symbol.Kind == SymbolKind.Method && ((IMethodSymbol)symbol).IsExtensionMethod;
      if (isExtensionMethod) {
        return (LuaExpressionSyntax)node.Accept(this);
      } else {
        var expression = BuildMemberAccessTargetExpression(node);
        if (symbol.IsStatic) {
          var typeSymbol = (INamedTypeSymbol)semanticModel_.GetTypeInfo(node).Type;
          if (symbol.ContainingType != typeSymbol) {
            bool isAssignment = node.Parent.Parent.Kind().IsAssignment();
            if (isAssignment || generator_.HasStaticCtor(typeSymbol) || generator_.HasStaticCtor(symbol.ContainingType)) {
              expression = GetTypeName(symbol.ContainingSymbol);
            }
          }
        }
        return expression;
      }
    }

    private LuaExpressionSyntax CheckMemberAccessCodeTemplate(ISymbol symbol, MemberAccessExpressionSyntax node) {
      if (symbol.Kind == SymbolKind.Field) {
        IFieldSymbol fieldSymbol = (IFieldSymbol)symbol;
        string codeTemplate = XmlMetaProvider.GetFieldCodeTemplate(fieldSymbol);
        if (codeTemplate != null) {
          return BuildCodeTemplateExpression(codeTemplate, node.Expression);
        }

        if (fieldSymbol.HasConstantValue) {
          return GetConstLiteralExpression(fieldSymbol);
        }
      } else if (symbol.Kind == SymbolKind.Property) {
        IPropertySymbol propertySymbol = (IPropertySymbol)symbol;
        bool isGet = !node.Parent.Kind().IsAssignment();
        string codeTemplate = XmlMetaProvider.GetProertyCodeTemplate(propertySymbol, isGet);
        if (codeTemplate != null) {
          return BuildCodeTemplateExpression(codeTemplate, node.Expression);
        }
      }
      return null;
    }

    public override LuaSyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
      ISymbol symbol = semanticModel_.GetSymbolInfo(node).Symbol;
      if (symbol == null) {  // dynamic
        var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
        var name = new LuaIdentifierNameSyntax(node.Name.Identifier.ValueText);
        return new LuaMemberAccessExpressionSyntax(expression, name, node.Parent.IsKind(SyntaxKind.InvocationExpression));
      }

      var codeTemplateExpression = CheckMemberAccessCodeTemplate(symbol, node);
      if (codeTemplateExpression != null) {
        return codeTemplateExpression;
      }

      if (symbol.Kind == SymbolKind.Property || symbol.Kind == SymbolKind.Event) {
        if (node.Expression.IsKind(SyntaxKind.ThisExpression)) {
          return node.Name.Accept(this);
        }

        if (node.Expression.IsKind(SyntaxKind.BaseExpression)) {
          var expression = (LuaIdentifierNameSyntax)node.Expression.Accept(this);
          var nameIdentifier = (LuaExpressionSyntax)node.Name.Accept(this);
          var propertyMethod = nameIdentifier as LuaPropertyAdapterExpressionSyntax;
          if (propertyMethod != null) {
            if (expression != LuaIdentifierNameSyntax.This) {
              propertyMethod.ArgumentList.AddArgument(LuaIdentifierNameSyntax.This);
            }
            propertyMethod.Update(expression, true);
            return propertyMethod;
          } else {
            return new LuaMemberAccessExpressionSyntax(expression, nameIdentifier);
          }
        } else {
          if (symbol.IsStatic) {
            if (node.Expression.IsKind(SyntaxKind.IdentifierName)) {
              var identifierName = (IdentifierNameSyntax)node.Expression;
              if (GetTypeDeclarationSymbol(node) == symbol.ContainingSymbol) {
                return node.Name.Accept(this);
              }
            }
          }

          var expression = BuildMemberAccessExpression(symbol, node.Expression);
          var nameExpression = (LuaExpressionSyntax)node.Name.Accept(this);
          return BuildFieldOrPropertyMemberAccessExpression(expression, nameExpression, symbol.IsStatic);
        }
      } else {
        if (node.Expression.IsKind(SyntaxKind.ThisExpression)) {
          return node.Name.Accept(this);
        }

        if (node.Expression.IsKind(SyntaxKind.BaseExpression)) {
          var baseExpression = (LuaExpressionSyntax)node.Expression.Accept(this);
          var identifier = (LuaIdentifierNameSyntax)node.Name.Accept(this);
          if (baseExpression == LuaIdentifierNameSyntax.This) {
            return new LuaMemberAccessExpressionSyntax(baseExpression, identifier, symbol.Kind == SymbolKind.Method);
          } else {
            LuaMemberAccessExpressionSyntax memberAccess = new LuaMemberAccessExpressionSyntax(baseExpression, identifier);
            return new LuaInternalMethodExpressionSyntax(memberAccess);
          }
        } else {
          if (symbol.IsStatic) {
            if (node.Expression.IsKind(SyntaxKind.IdentifierName)) {
              var identifierName = (IdentifierNameSyntax)node.Expression;
              if (GetTypeDeclarationSymbol(node) == symbol.ContainingSymbol) {
                return node.Name.Accept(this);
              }
            }
          } else if (symbol.Kind == SymbolKind.Method) {
            if (!node.Parent.IsKind(SyntaxKind.InvocationExpression)) {
              var target = (LuaExpressionSyntax)node.Expression.Accept(this);
              var methodName = (LuaExpressionSyntax)node.Name.Accept(this);
              if (!IsInternalMember(node.Name, symbol)) {
                methodName = new LuaMemberAccessExpressionSyntax(target, methodName);
              }
              return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.DelegateBind, target, methodName);
            }
          }

          var expression = BuildMemberAccessExpression(symbol, node.Expression);
          var identifier = (LuaExpressionSyntax)node.Name.Accept(this);
          return new LuaMemberAccessExpressionSyntax(expression, identifier, !symbol.IsStatic && symbol.Kind == SymbolKind.Method);
        }
      }
    }

    private LuaExpressionSyntax BuildStaticFieldName(ISymbol symbol, bool isReadOnly, IdentifierNameSyntax node) {
      Contract.Assert(symbol.IsStatic);
      LuaIdentifierNameSyntax name = GetMemberName(symbol);
      if (!symbol.IsPrivate()) {
        if (isReadOnly) {
          if (node.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
            AssignmentExpressionSyntax assignmentExpression = (AssignmentExpressionSyntax)node.Parent;
            if (assignmentExpression.Left == node) {
              CurType.AddStaticReadOnlyAssignmentName(name);
            }
          }
          var usingStaticType = CheckUsingStaticNameSyntax(symbol, node);
          if (usingStaticType != null) {
            return new LuaMemberAccessExpressionSyntax(usingStaticType, name);
          }
        } else {
          if (IsInternalNode(node)) {
            var constructor = CurFunctionOrNull as LuaConstructorAdapterExpressionSyntax;
            if (constructor != null) {
              return new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, name);
            } else {
              var typeName = GetTypeName(symbol.ContainingType);
              return new LuaMemberAccessExpressionSyntax(typeName, name);
            }
          } else {
            var usingStaticType = CheckUsingStaticNameSyntax(symbol, node);
            if (usingStaticType != null) {
              return new LuaMemberAccessExpressionSyntax(usingStaticType, name);
            }
          }
        }
      }
      return name;
    }

    private bool IsInternalNode(NameSyntax node) {
      var parentNode = node.Parent;
      switch (parentNode.Kind()) {
        case SyntaxKind.SimpleMemberAccessExpression: {
            MemberAccessExpressionSyntax parent = (MemberAccessExpressionSyntax)parentNode;
            if (parent.Expression.IsKind(SyntaxKind.ThisExpression)) {
              return true;
            } else if (parent.Expression == node) {
              return true;
            }
            return false;
          }
        case SyntaxKind.MemberBindingExpression:
        case SyntaxKind.NameEquals: {
            return false;
          }
        case SyntaxKind.SimpleAssignmentExpression: {
            AssignmentExpressionSyntax parent = (AssignmentExpressionSyntax)parentNode;
            if (parent.Right != node) {
              if (parent.Parent.IsKind(SyntaxKind.ObjectInitializerExpression)) {
                return false;
              }
            }
            break;
          }
      }
      return true;
    }

    private LuaExpressionSyntax VisitFieldOrEventIdentifierName(IdentifierNameSyntax node, ISymbol symbol, bool isProperty) {
      bool isField, isReadOnly;
      if (isProperty) {
        var propertySymbol = (IPropertySymbol)symbol;
        isField = IsPropertyField(propertySymbol);
        isReadOnly = propertySymbol.IsReadOnly;
      } else {
        var eventSymbol = (IEventSymbol)symbol;
        isField = IsEventFiled(eventSymbol);
        isReadOnly = false;
      }

      if (symbol.IsStatic) {
        if (isField) {
          return BuildStaticFieldName(symbol, isReadOnly, node);
        } else {
          var name = GetMemberName(symbol);
          var identifierExpression = new LuaPropertyAdapterExpressionSyntax(new LuaPropertyOrEventIdentifierNameSyntax(isProperty, name));
          var usingStaticType = CheckUsingStaticNameSyntax(symbol, node);
          if (usingStaticType != null) {
            return new LuaMemberAccessExpressionSyntax(usingStaticType, identifierExpression);
          }
          return identifierExpression;
        }
      } else {
        if (isField) {
          LuaIdentifierNameSyntax fieldName = GetMemberName(symbol);
          if (IsInternalNode(node)) {
            return new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, fieldName);
          } else {
            return fieldName;
          }
        } else {
          var name = GetMemberName(symbol);
          if (IsInternalMember(node, symbol)) {
            var propertyAdapter = new LuaPropertyAdapterExpressionSyntax(new LuaPropertyOrEventIdentifierNameSyntax(isProperty, name));
            propertyAdapter.ArgumentList.AddArgument(LuaIdentifierNameSyntax.This);
            return propertyAdapter;
          } else {
            if (IsInternalNode(node)) {
              LuaPropertyOrEventIdentifierNameSyntax identifierName = new LuaPropertyOrEventIdentifierNameSyntax(isProperty, name);
              return new LuaPropertyAdapterExpressionSyntax(LuaIdentifierNameSyntax.This, identifierName, true);
            } else {
              return new LuaPropertyAdapterExpressionSyntax(new LuaPropertyOrEventIdentifierNameSyntax(isProperty, name));
            }
          }
        }
      }
    }

    private LuaExpressionSyntax GetMethodNameExpression(IMethodSymbol symbol, NameSyntax node) {
      LuaIdentifierNameSyntax methodName = GetMemberName(symbol);
      if (symbol.IsStatic) {
        var usingStaticType = CheckUsingStaticNameSyntax(symbol, node);
        if (usingStaticType != null) {
          return new LuaMemberAccessExpressionSyntax(usingStaticType, methodName);
        }
        if (IsInternalMember(node, symbol)) {
          return new LuaInternalMethodExpressionSyntax(methodName);
        }
        return methodName;
      } else {
        if (IsInternalMember(node, symbol)) {
          if (!node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression)
             && !node.Parent.IsKind(SyntaxKind.InvocationExpression)) {
            return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.DelegateBind, LuaIdentifierNameSyntax.This, methodName);
          }
          return new LuaInternalMethodExpressionSyntax(methodName);
        } else {
          if (IsInternalNode(node)) {
            if (!node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                && !node.Parent.IsKind(SyntaxKind.InvocationExpression)) {
              return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.DelegateBind, LuaIdentifierNameSyntax.This, new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, methodName));
            }

            LuaMemberAccessExpressionSyntax memberAccess = new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, methodName, true);
            return memberAccess;
          }
        }
      }
      return methodName;
    }

    private LuaExpressionSyntax GetFieldNameExpression(IFieldSymbol symbol, IdentifierNameSyntax node) {
      if (symbol.IsStatic) {
        if (symbol.HasConstantValue) {
          if (symbol.Type.SpecialType == SpecialType.System_String) {
            if (((string)symbol.ConstantValue).Length < LuaSyntaxNode.StringConstInlineCount) {
              return GetConstLiteralExpression(symbol);
            }
          } else {
            return GetConstLiteralExpression(symbol);
          }
        }
        return BuildStaticFieldName(symbol, symbol.IsReadOnly, node);
      } else {
        if (IsInternalNode(node)) {
          if (symbol.IsPrivate() && symbol.IsFromCode()) {
            string symbolName = symbol.Name;
            bool success = CheckFieldNameOfProtobufnet(ref symbolName, symbol.ContainingType);
            if (success) {
              return new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, new LuaIdentifierNameSyntax(symbolName));
            }
          }
          return new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.This, GetMemberName(symbol));
        } else {
          return GetMemberName(symbol);
        }
      }
    }

    public override LuaSyntaxNode VisitIdentifierName(IdentifierNameSyntax node) {
      SymbolInfo symbolInfo = semanticModel_.GetSymbolInfo(node);
      ISymbol symbol = symbolInfo.Symbol;
      Contract.Assert(symbol != null);
      LuaExpressionSyntax identifier;
      switch (symbol.Kind) {
        case SymbolKind.Local:
        case SymbolKind.Parameter:
        case SymbolKind.RangeVariable: {
            string name = symbol.Name;
            CheckReservedWord(ref name, symbol);
            identifier = new LuaIdentifierNameSyntax(name);
            break;
          }
        case SymbolKind.TypeParameter:
        case SymbolKind.Label: {
            identifier = new LuaIdentifierNameSyntax(symbol.Name);
            break;
          }
        case SymbolKind.NamedType: {
            if (node.Parent.IsKind(SyntaxKind.SimpleMemberAccessExpression)) {
              var parent = (MemberAccessExpressionSyntax)node.Parent;
              if (parent.Name == node) {
                identifier = new LuaIdentifierNameSyntax(symbol.Name);
                break;
              }
            }
            identifier = GetTypeName(symbol);
            break;
          }
        case SymbolKind.Namespace: {
            identifier = new LuaIdentifierNameSyntax(symbol.ToString());
            break;
          }
        case SymbolKind.Field: {
            identifier = GetFieldNameExpression((IFieldSymbol)symbol, node);
            break;
          }
        case SymbolKind.Method: {
            identifier = GetMethodNameExpression((IMethodSymbol)symbol, node);
            break;
          }
        case SymbolKind.Property: {
            identifier = VisitFieldOrEventIdentifierName(node, symbol, true);
            break;
          }
        case SymbolKind.Event: {
            identifier = VisitFieldOrEventIdentifierName(node, symbol, false);
            break;
          }
        default: {
            throw new NotSupportedException();
          }
      }
      CheckConversion(node, ref identifier);
      return identifier;
    }

    public override LuaSyntaxNode VisitQualifiedName(QualifiedNameSyntax node) {
      return node.Right.Accept(this);
    }

    private void FillInvocationArgument(List<LuaExpressionSyntax> arguments, ArgumentSyntax node, ImmutableArray<IParameterSymbol> parameters, List<LuaExpressionSyntax> refOrOutArguments) {
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      Contract.Assert(expression != null);
      if (node.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword)) {
        refOrOutArguments.Add(expression);
      } else if (node.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)) {
        refOrOutArguments.Add(expression);
        expression = LuaIdentifierNameSyntax.Nil;
      } else {
        CheckValueTypeAndConversion(node.Expression, ref expression);
      }
      if (node.NameColon != null) {
        string name = node.NameColon.Name.Identifier.ValueText;
        int index = parameters.IndexOf(i => i.Name == name);
        if (index == -1) {
          throw new InvalidOperationException();
        }
        arguments.AddAt(index, expression);
      } else {
        arguments.Add(expression);
      }
    }

    private List<LuaExpressionSyntax> BuildArgumentList(ISymbol symbol, ImmutableArray<IParameterSymbol> parameters, BaseArgumentListSyntax node, List<LuaExpressionSyntax> refOrOutArguments = null) {
      List<LuaExpressionSyntax> arguments = new List<LuaExpressionSyntax>();
      foreach (var argument in node.Arguments) {
        FillInvocationArgument(arguments, argument, parameters, refOrOutArguments);
      }
      CheckInvocationDeafultArguments(symbol, parameters, arguments, node);
      return arguments;
    }

    private LuaArgumentListSyntax BuildArgumentList(SeparatedSyntaxList<ArgumentSyntax> arguments) {
      LuaArgumentListSyntax argumentList = new LuaArgumentListSyntax();
      foreach (var argument in arguments) {
        var newNode = (LuaArgumentSyntax)argument.Accept(this);
        argumentList.Arguments.Add(newNode);
      }
      return argumentList;
    }

    public override LuaSyntaxNode VisitArgumentList(ArgumentListSyntax node) {
      return BuildArgumentList(node.Arguments);
    }

    public override LuaSyntaxNode VisitArgument(ArgumentSyntax node) {
      Contract.Assert(node.NameColon == null);
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      return new LuaArgumentSyntax(expression);
    }

    public override LuaSyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node) {
      switch (node.Kind()) {
        case SyntaxKind.NumericLiteralExpression: {
            return new LuaIdentifierLiteralExpressionSyntax(node.Token.Text);
          }
        case SyntaxKind.StringLiteralExpression: {
            return BuildStringLiteralTokenExpression(node.Token);
          }
        case SyntaxKind.CharacterLiteralExpression: {
            return new LuaCharacterLiteralExpression((char)node.Token.Value);
          }
        case SyntaxKind.NullLiteralExpression: {
            return LuaIdentifierLiteralExpressionSyntax.Nil;
          }
        default: {
            return new LuaIdentifierLiteralExpressionSyntax(node.Token.ValueText);
          }
      }
    }

    public override LuaSyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) {
      var declaration = (LuaVariableDeclarationSyntax)node.Declaration.Accept(this);
      return new LuaLocalDeclarationStatementSyntax(declaration);
    }

    public override LuaSyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node) {
      LuaVariableListDeclarationSyntax variableListDeclaration = new LuaVariableListDeclarationSyntax();
      foreach (VariableDeclaratorSyntax variable in node.Variables) {
        var variableDeclarator = (LuaVariableDeclaratorSyntax)variable.Accept(this);
        variableListDeclaration.Variables.Add(variableDeclarator);
      }
      bool isMultiNil = variableListDeclaration.Variables.Count > 0 && variableListDeclaration.Variables.All(i => i.Initializer == null);
      if (isMultiNil) {
        LuaLocalVariablesStatementSyntax declarationStatement = new LuaLocalVariablesStatementSyntax();
        foreach (var variable in variableListDeclaration.Variables) {
          declarationStatement.Variables.Add(variable.Identifier);
        }
        return declarationStatement;
      } else {
        return variableListDeclaration;
      }
    }

    public override LuaSyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node) {
      LuaIdentifierNameSyntax identifier = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      CheckVariableDeclaratorName(ref identifier, node);
      LuaVariableDeclaratorSyntax variableDeclarator = new LuaVariableDeclaratorSyntax(identifier);
      if (node.Initializer != null) {
        variableDeclarator.Initializer = (LuaEqualsValueClauseSyntax)node.Initializer.Accept(this);
      }
      return variableDeclarator;
    }

    public override LuaSyntaxNode VisitEqualsValueClause(EqualsValueClauseSyntax node) {
      ExpressionSyntax value;
      if (node.Value.Kind().IsAssignment()) {
        var assignmentExpression = (AssignmentExpressionSyntax)node.Value;
        var assignment = (LuaExpressionSyntax)VisitAssignmentExpression(assignmentExpression);
        CurBlock.Statements.Add(new LuaExpressionStatementSyntax(assignment));
        value = assignmentExpression.Left;
      } else {
        value = node.Value;
      }
      var expression = (LuaExpressionSyntax)value.Accept(this);
      CheckConversion(node.Value, ref expression);
      return new LuaEqualsValueClauseSyntax(expression);
    }

    public override LuaSyntaxNode VisitPredefinedType(PredefinedTypeSyntax node) {
      ISymbol symbol = semanticModel_.GetSymbolInfo(node).Symbol;
      return GetTypeShortName(symbol);
    }

    private void WriteStatementOrBlock(StatementSyntax statement, LuaBlockSyntax block) {
      if (statement.IsKind(SyntaxKind.Block)) {
        var blockNode = (LuaBlockSyntax)statement.Accept(this);
        block.Statements.AddRange(blockNode.Statements);
      } else {
        blocks_.Push(block);
        var statementNode = (LuaStatementSyntax)statement.Accept(this);
        block.Statements.Add(statementNode);
        blocks_.Pop();
      }
    }

    #region if else switch

    public override LuaSyntaxNode VisitIfStatement(IfStatementSyntax node) {
      var condition = (LuaExpressionSyntax)node.Condition.Accept(this);
      LuaIfStatementSyntax ifStatement = new LuaIfStatementSyntax(condition);
      WriteStatementOrBlock(node.Statement, ifStatement.Body);
      ifStatements_.Push(ifStatement);
      node.Else?.Accept(this);
      ifStatements_.Pop();
      return ifStatement;
    }

    public override LuaSyntaxNode VisitElseClause(ElseClauseSyntax node) {
      if (node.Statement.IsKind(SyntaxKind.IfStatement)) {
        var ifStatement = (IfStatementSyntax)node.Statement;
        var condition = (LuaExpressionSyntax)ifStatement.Condition.Accept(this);
        LuaElseIfStatementSyntax elseIfStatement = new LuaElseIfStatementSyntax(condition);
        WriteStatementOrBlock(ifStatement.Statement, elseIfStatement.Body);
        ifStatements_.Peek().ElseIfStatements.Add(elseIfStatement);
        ifStatement.Else?.Accept(this);
        return elseIfStatement;
      } else {
        LuaElseClauseSyntax elseClause = new LuaElseClauseSyntax();
        WriteStatementOrBlock(node.Statement, elseClause.Body);
        ifStatements_.Peek().Else = elseClause;
        return elseClause;
      }
    }

    public override LuaSyntaxNode VisitSwitchStatement(SwitchStatementSyntax node) {
      var temp = GetTempIdentifier(node);
      LuaSwitchAdapterStatementSyntax switchStatement = new LuaSwitchAdapterStatementSyntax(temp);
      switchs_.Push(switchStatement);
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      switchStatement.Fill(expression, node.Sections.Select(i => (LuaStatementSyntax)i.Accept(this)));
      switchs_.Pop();
      return switchStatement;
    }

    public override LuaSyntaxNode VisitSwitchSection(SwitchSectionSyntax node) {
      bool isDefault = node.Labels.Any(i => i.Kind() == SyntaxKind.DefaultSwitchLabel);
      if (isDefault) {
        LuaBlockSyntax block = new LuaBlockSyntax();
        foreach (var statement in node.Statements) {
          var luaStatement = (LuaStatementSyntax)statement.Accept(this);
          block.Statements.Add(luaStatement);
        }
        return block;
      } else {
        var expressions = node.Labels.Select(i => (LuaExpressionSyntax)i.Accept(this));
        var condition = expressions.Aggregate((x, y) => new LuaBinaryExpressionSyntax(x, LuaSyntaxNode.Tokens.Or, y));
        LuaIfStatementSyntax ifStatement = new LuaIfStatementSyntax(condition);
        foreach (var statement in node.Statements) {
          var luaStatement = (LuaStatementSyntax)statement.Accept(this);
          ifStatement.Body.Statements.Add(luaStatement);
        }
        return ifStatement;
      }
    }

    public override LuaSyntaxNode VisitCaseSwitchLabel(CaseSwitchLabelSyntax node) {
      var left = switchs_.Peek().Temp;
      var right = (LuaExpressionSyntax)node.Value.Accept(this);
      LuaBinaryExpressionSyntax BinaryExpression = new LuaBinaryExpressionSyntax(left, LuaSyntaxNode.Tokens.EqualsEquals, right);
      return BinaryExpression;
    }

    #endregion

    public override LuaSyntaxNode VisitBreakStatement(BreakStatementSyntax node) {
      return LuaBreakStatementSyntax.Statement;
    }

    private LuaExpressionSyntax WrapStringConcatExpression(ExpressionSyntax expression) {
      ITypeSymbol typeInfo = semanticModel_.GetTypeInfo(expression).Type;
      var original = (LuaExpressionSyntax)expression.Accept(this);
      if (typeInfo.IsStringType()) {
        return original;
      } else if (typeInfo.SpecialType == SpecialType.System_Char) {
        var constValue = semanticModel_.GetConstantValue(expression);
        if (constValue.HasValue) {
          string text = SyntaxFactory.Literal((char)constValue.Value).Text;
          return new LuaIdentifierLiteralExpressionSyntax(text);
        } else {
          return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.StringChar, original);
        }
      } else if (typeInfo.SpecialType >= SpecialType.System_Boolean && typeInfo.SpecialType <= SpecialType.System_Double) {
        return original;
      } else if (typeInfo.TypeKind == TypeKind.Enum) {
        if (original is LuaLiteralExpressionSyntax) {
          var symbol = semanticModel_.GetSymbolInfo(expression).Symbol;
          return new LuaConstLiteralExpression(symbol.Name, typeInfo.ToString());
        } else {
          AddExportEnum(typeInfo);
          LuaIdentifierNameSyntax typeName = GetTypeShortName(typeInfo);
          LuaMemberAccessExpressionSyntax memberAccess = new LuaMemberAccessExpressionSyntax(original, LuaIdentifierNameSyntax.ToEnumString, true);
          return new LuaInvocationExpressionSyntax(memberAccess, typeName);
        }
      } else if (typeInfo.IsValueType) {
        LuaMemberAccessExpressionSyntax memberAccess = new LuaMemberAccessExpressionSyntax(original, LuaIdentifierNameSyntax.ToStr, true);
        return new LuaInvocationExpressionSyntax(memberAccess);
      } else {
        return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.SystemToString, original);
      }
    }

    private LuaExpressionSyntax BuildStringConcatExpression(BinaryExpressionSyntax node) {
      var expression = BuildStringConcatExpression(node.Left, node.Right);
      if (node.Parent.IsKind(SyntaxKind.AddExpression)) {
        expression = new LuaParenthesizedExpressionSyntax(expression);
      }
      return expression;
    }

    private LuaExpressionSyntax BuildStringConcatExpression(ExpressionSyntax leftNode, ExpressionSyntax rightNode) {
      var left = WrapStringConcatExpression(leftNode);
      var right = WrapStringConcatExpression(rightNode);
      return new LuaBinaryExpressionSyntax(left, LuaSyntaxNode.Tokens.Concatenation, right);
    }

    private LuaExpressionSyntax BuildBinaryInvokeExpression(BinaryExpressionSyntax node, LuaIdentifierNameSyntax name) {
      var left = (LuaExpressionSyntax)node.Left.Accept(this);
      var right = (LuaExpressionSyntax)node.Right.Accept(this);
      return new LuaInvocationExpressionSyntax(name, left, right);
    }

    private LuaExpressionSyntax BuildIntegerDivExpression(BinaryExpressionSyntax node) {
      if (IsLuaNewest) {
        return BuildBinaryExpression(node, LuaSyntaxNode.Tokens.IntegerDiv);
      } else {
        return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.IntegerDiv);
      }
    }

    private LuaBinaryExpressionSyntax BuildBinaryExpression(BinaryExpressionSyntax node, string operatorToken) {
      var left = (LuaExpressionSyntax)node.Left.Accept(this);
      var right = (LuaExpressionSyntax)node.Right.Accept(this);
      return new LuaBinaryExpressionSyntax(left, operatorToken, right);
    }

    private LuaExpressionSyntax BuildBitExpression(BinaryExpressionSyntax node, string boolOperatorToken, LuaIdentifierNameSyntax otherName) {
      var leftType = semanticModel_.GetTypeInfo(node.Left).Type;
      if (leftType.SpecialType == SpecialType.System_Boolean) {
        return BuildBinaryExpression(node, boolOperatorToken);
      } else if (!IsLuaNewest) {
        return BuildBinaryInvokeExpression(node, otherName);
      } else {
        string operatorToken = GetOperatorToken(node.OperatorToken);
        return BuildBinaryExpression(node, operatorToken);
      }
    }

    public override LuaSyntaxNode VisitBinaryExpression(BinaryExpressionSyntax node) {
      var constExpression = GetConstExpression(node);
      if (constExpression != null) {
        return constExpression;
      }

      switch (node.Kind()) {
        case SyntaxKind.AddExpression: {
            var methodSymbol = semanticModel_.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol != null) {
              if (methodSymbol.ContainingType.IsStringType()) {
                return BuildStringConcatExpression(node);
              } else if (methodSymbol.ContainingType.IsDelegateType()) {
                return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.DelegateCombine);
              }
            }
            break;
          }
        case SyntaxKind.SubtractExpression: {
            var methodSymbol = semanticModel_.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (methodSymbol != null && methodSymbol.ContainingType.IsDelegateType()) {
              return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.DelegateRemove);
            }
            break;
          }
        case SyntaxKind.DivideExpression: {
            var leftType = semanticModel_.GetTypeInfo(node.Left).Type;
            var rightType = semanticModel_.GetTypeInfo(node.Right).Type;
            if (leftType.IsIntegerType() && rightType.IsIntegerType()) {
              return BuildIntegerDivExpression(node);
            }
            break;
          }
        case SyntaxKind.ModuloExpression: {
            if (!IsLuaNewest) {
              var leftType = semanticModel_.GetTypeInfo(node.Left).Type;
              var rightType = semanticModel_.GetTypeInfo(node.Right).Type;
              if (leftType.IsIntegerType() && rightType.IsIntegerType()) {
                return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.IntegerMod);
              }
            }
            break;
          }
        case SyntaxKind.LeftShiftExpression: {
            if (!IsLuaNewest) {
              return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.ShiftLeft);
            }
            break;
          }
        case SyntaxKind.RightShiftExpression: {
            if (!IsLuaNewest) {
              return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.ShiftRight);
            }
            break;
          }
        case SyntaxKind.BitwiseOrExpression: {
            return BuildBitExpression(node, LuaSyntaxNode.Tokens.Or, LuaIdentifierNameSyntax.BitOr);
          }
        case SyntaxKind.BitwiseAndExpression: {
            return BuildBitExpression(node, LuaSyntaxNode.Tokens.And, LuaIdentifierNameSyntax.BitAnd);
          }
        case SyntaxKind.ExclusiveOrExpression: {
            return BuildBitExpression(node, LuaSyntaxNode.Tokens.NotEquals, LuaIdentifierNameSyntax.BitXor);
          }
        case SyntaxKind.IsExpression: {
            var leftType = semanticModel_.GetTypeInfo(node.Left).Type;
            var rightType = semanticModel_.GetTypeInfo(node.Right).Type;
            if (leftType.IsSubclassOf(rightType)) {
              return LuaIdentifierLiteralExpressionSyntax.True;
            }

            return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.Is);
          }
        case SyntaxKind.AsExpression: {
            var leftType = semanticModel_.GetTypeInfo(node.Left).Type;
            var rightType = semanticModel_.GetTypeInfo(node.Right).Type;
            if (leftType.IsSubclassOf(rightType)) {
              return node.Left.Accept(this);
            }

            return BuildBinaryInvokeExpression(node, LuaIdentifierNameSyntax.As);
          }
      }

      var operatorExpression = GerUserDefinedOperatorExpression(node);
      if (operatorExpression != null) {
        return operatorExpression;
      }

      string operatorToken = GetOperatorToken(node.OperatorToken);
      return BuildBinaryExpression(node, operatorToken);
    }

    private bool IsSingleLineUnary(ExpressionSyntax node) {
      return node.Parent.IsKind(SyntaxKind.ExpressionStatement) || node.Parent.IsKind(SyntaxKind.ForStatement);
    }

    private LuaSyntaxNode BuildPrefixUnaryExpression(bool isSingleLine, string operatorToken, LuaExpressionSyntax operand, SyntaxNode node, bool isLocalVar = false) {
      LuaBinaryExpressionSyntax binary = new LuaBinaryExpressionSyntax(operand, operatorToken, LuaIdentifierNameSyntax.One);
      if (isSingleLine) {
        return new LuaAssignmentExpressionSyntax(operand, binary);
      } else {
        if (isLocalVar) {
          CurBlock.Statements.Add(new LuaExpressionStatementSyntax(new LuaAssignmentExpressionSyntax(operand, binary)));
          return operand;
        } else {
          var temp = GetTempIdentifier(node);
          CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(temp, binary));
          CurBlock.Statements.Add(new LuaExpressionStatementSyntax(new LuaAssignmentExpressionSyntax(operand, temp)));
          return temp;
        }
      }
    }

    private LuaSyntaxNode BuildPropertyPrefixUnaryExpression(bool isSingleLine, string operatorToken, LuaPropertyAdapterExpressionSyntax get, LuaPropertyAdapterExpressionSyntax set, SyntaxNode node) {
      set.IsGetOrAdd = false;
      var binary = new LuaBinaryExpressionSyntax(get, operatorToken, LuaIdentifierNameSyntax.One);
      if (isSingleLine) {
        set.ArgumentList.AddArgument(binary);
        return set;
      } else {
        var temp = GetTempIdentifier(node);
        CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(temp, binary));
        set.ArgumentList.AddArgument(temp);
        CurBlock.Statements.Add(new LuaExpressionStatementSyntax(set));
        return temp;
      }
    }

    private LuaMemberAccessExpressionSyntax GetTempUnaryExpression(LuaMemberAccessExpressionSyntax memberAccess, out LuaLocalVariableDeclaratorSyntax localTemp, SyntaxNode node) {
      var temp = GetTempIdentifier(node);
      localTemp = new LuaLocalVariableDeclaratorSyntax(temp, memberAccess.Expression);
      return new LuaMemberAccessExpressionSyntax(temp, memberAccess.Name, memberAccess.IsObjectColon);
    }

    private LuaPropertyAdapterExpressionSyntax GetTempPropertyUnaryExpression(LuaPropertyAdapterExpressionSyntax propertyAdapter, out LuaLocalVariableDeclaratorSyntax localTemp, SyntaxNode node) {
      var temp = GetTempIdentifier(node);
      localTemp = new LuaLocalVariableDeclaratorSyntax(temp, propertyAdapter.Expression);
      return new LuaPropertyAdapterExpressionSyntax(temp, propertyAdapter.Name, propertyAdapter.IsObjectColon);
    }

    public override LuaSyntaxNode VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node) {
      SyntaxKind kind = node.Kind();
      switch(kind) {
        case SyntaxKind.PreIncrementExpression:
        case SyntaxKind.PreDecrementExpression: {
            bool isSingleLine = IsSingleLineUnary(node);
            string operatorToken = kind == SyntaxKind.PreIncrementExpression ? LuaSyntaxNode.Tokens.Plus : LuaSyntaxNode.Tokens.Sub;
            var operand = (LuaExpressionSyntax)node.Operand.Accept(this);

            if (operand is LuaMemberAccessExpressionSyntax) {
              var memberAccess = (LuaMemberAccessExpressionSyntax)operand;
              if (memberAccess.Expression != LuaIdentifierNameSyntax.This) {
                LuaLocalVariableDeclaratorSyntax localTemp;
                memberAccess = GetTempUnaryExpression(memberAccess, out localTemp, node);
                CurBlock.Statements.Add(localTemp);
              }
              return BuildPrefixUnaryExpression(isSingleLine, operatorToken, memberAccess, node);
            } else if (operand is LuaPropertyAdapterExpressionSyntax) {
              var propertyAdapter = (LuaPropertyAdapterExpressionSyntax)operand;
              if (propertyAdapter.Expression != null) {
                LuaLocalVariableDeclaratorSyntax localTemp;
                var getAdapter = GetTempPropertyUnaryExpression(propertyAdapter, out localTemp, node);
                CurBlock.Statements.Add(localTemp);
                return BuildPropertyPrefixUnaryExpression(isSingleLine, operatorToken, getAdapter, getAdapter.GetClone(), node);
              } else {
                return BuildPropertyPrefixUnaryExpression(isSingleLine, operatorToken, propertyAdapter, propertyAdapter.GetClone(), node);
              }
            } else {
              bool isLocalVar = false;
              if (!isSingleLine) {
                SymbolKind symbolKind = semanticModel_.GetSymbolInfo(node.Operand).Symbol.Kind;
                if (symbolKind == SymbolKind.Parameter || symbolKind == SymbolKind.Local) {
                  isLocalVar = true;
                }
              }
              return BuildPrefixUnaryExpression(isSingleLine, operatorToken, operand, node, isLocalVar);
            }
          }
        case SyntaxKind.PointerIndirectionExpression: {
            var operand = (LuaExpressionSyntax)node.Operand.Accept(this);
            var identifier = new LuaPropertyOrEventIdentifierNameSyntax(true, LuaIdentifierNameSyntax.Empty);
            return new LuaPropertyAdapterExpressionSyntax(operand, identifier, true);
          }
        default: {
            var operand = (LuaExpressionSyntax)node.Operand.Accept(this);
            string operatorToken = GetOperatorToken(node.OperatorToken);
            LuaPrefixUnaryExpressionSyntax unaryExpression = new LuaPrefixUnaryExpressionSyntax(operand, operatorToken);
            return unaryExpression;
          }
      }
    }

    private LuaSyntaxNode BuildPostfixUnaryExpression(bool isSingleLine, string operatorToken, LuaExpressionSyntax operand, SyntaxNode node) {
      if (isSingleLine) {
        LuaBinaryExpressionSyntax binary = new LuaBinaryExpressionSyntax(operand, operatorToken, LuaIdentifierNameSyntax.One);
        return new LuaAssignmentExpressionSyntax(operand, binary);
      } else {
        var temp = GetTempIdentifier(node);
        CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(temp, operand));
        LuaBinaryExpressionSyntax binary = new LuaBinaryExpressionSyntax(temp, operatorToken, LuaIdentifierNameSyntax.One);
        CurBlock.Statements.Add(new LuaExpressionStatementSyntax(new LuaAssignmentExpressionSyntax(operand, binary)));
        return temp;
      }
    }

    private LuaSyntaxNode BuildPropertyPostfixUnaryExpression(bool isSingleLine, string operatorToken, LuaPropertyAdapterExpressionSyntax get, LuaPropertyAdapterExpressionSyntax set, SyntaxNode node) {
      set.IsGetOrAdd = false;
      if (isSingleLine) {
        LuaBinaryExpressionSyntax binary = new LuaBinaryExpressionSyntax(get, operatorToken, LuaIdentifierNameSyntax.One);
        return new LuaAssignmentExpressionSyntax(set, binary);
      } else {
        var temp = GetTempIdentifier(node);
        CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(temp, get));
        LuaBinaryExpressionSyntax binary = new LuaBinaryExpressionSyntax(temp, operatorToken, LuaIdentifierNameSyntax.One);
        set.ArgumentList.AddArgument(binary);
        CurBlock.Statements.Add(new LuaExpressionStatementSyntax(set));
        return temp;
      }
    }

    public override LuaSyntaxNode VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node) {
      SyntaxKind kind = node.Kind();
      if (kind != SyntaxKind.PostIncrementExpression && kind != SyntaxKind.PostDecrementExpression) {
        throw new NotSupportedException();
      }

      bool isSingleLine = IsSingleLineUnary(node);
      string operatorToken = kind == SyntaxKind.PostIncrementExpression ? LuaSyntaxNode.Tokens.Plus : LuaSyntaxNode.Tokens.Sub;
      var operand = (LuaExpressionSyntax)node.Operand.Accept(this);

      if (operand is LuaMemberAccessExpressionSyntax) {
        var memberAccess = (LuaMemberAccessExpressionSyntax)operand;
        if (memberAccess.Expression != LuaIdentifierNameSyntax.This) {
          LuaLocalVariableDeclaratorSyntax localTemp;
          memberAccess = GetTempUnaryExpression(memberAccess, out localTemp, node);
          CurBlock.Statements.Add(localTemp);
        }
        return BuildPostfixUnaryExpression(isSingleLine, operatorToken, memberAccess, node);
      } else if (operand is LuaPropertyAdapterExpressionSyntax) {
        var propertyAdapter = (LuaPropertyAdapterExpressionSyntax)operand;
        if (propertyAdapter.Expression != null) {
          LuaLocalVariableDeclaratorSyntax localTemp;
          var getAdapter = GetTempPropertyUnaryExpression(propertyAdapter, out localTemp, node);
          CurBlock.Statements.Add(localTemp);
          return BuildPropertyPostfixUnaryExpression(isSingleLine, operatorToken, getAdapter, getAdapter.GetClone(), node);
        } else {
          return BuildPropertyPostfixUnaryExpression(isSingleLine, operatorToken, propertyAdapter, propertyAdapter.GetClone(), node);
        }
      } else {
        return BuildPostfixUnaryExpression(isSingleLine, operatorToken, operand, node);
      }
    }

    public override LuaSyntaxNode VisitContinueStatement(ContinueStatementSyntax node) {
      return LuaContinueAdapterStatementSyntax.Statement;
    }

    private void VisitLoopBody(StatementSyntax bodyStatement, LuaBlockSyntax block) {
      bool hasContinue = IsContinueExists(bodyStatement);
      if (hasContinue) {
        // http://lua-users.org/wiki/ContinueProposal
        var continueIdentifier = LuaIdentifierNameSyntax.Continue;
        block.Statements.Add(new LuaLocalVariableDeclaratorSyntax(new LuaVariableDeclaratorSyntax(continueIdentifier)));
        LuaRepeatStatementSyntax repeatStatement = new LuaRepeatStatementSyntax(LuaIdentifierNameSyntax.One);
        WriteStatementOrBlock(bodyStatement, repeatStatement.Body);
        LuaAssignmentExpressionSyntax assignment = new LuaAssignmentExpressionSyntax(continueIdentifier, LuaIdentifierNameSyntax.True);
        repeatStatement.Body.Statements.Add(new LuaExpressionStatementSyntax(assignment));
        block.Statements.Add(repeatStatement);
        LuaIfStatementSyntax IfStatement = new LuaIfStatementSyntax(new LuaPrefixUnaryExpressionSyntax(continueIdentifier, LuaSyntaxNode.Tokens.Not));
        IfStatement.Body.Statements.Add(LuaBreakStatementSyntax.Statement);
        block.Statements.Add(IfStatement);
      } else {
        WriteStatementOrBlock(bodyStatement, block);
      }
    }

    public override LuaSyntaxNode VisitForEachStatement(ForEachStatementSyntax node) {
      LuaIdentifierNameSyntax identifier = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      LuaForInStatementSyntax forInStatement = new LuaForInStatementSyntax(identifier, expression);
      VisitLoopBody(node.Statement, forInStatement.Body);
      return forInStatement;
    }

    public override LuaSyntaxNode VisitWhileStatement(WhileStatementSyntax node) {
      var condition = (LuaExpressionSyntax)node.Condition.Accept(this);
      LuaWhileStatementSyntax whileStatement = new LuaWhileStatementSyntax(condition);
      VisitLoopBody(node.Statement, whileStatement.Body);
      return whileStatement;
    }

    public override LuaSyntaxNode VisitForStatement(ForStatementSyntax node) {
      var numericalForStatement = GetNumericalForStatement(node);
      if (numericalForStatement != null) {
        return numericalForStatement;
      }

      LuaBlockSyntax block = new LuaBlockStatementSyntax();
      blocks_.Push(block);

      if (node.Declaration != null) {
        block.Statements.Add((LuaVariableDeclarationSyntax)node.Declaration.Accept(this));
      }
      var initializers = node.Initializers.Select(i => new LuaExpressionStatementSyntax((LuaExpressionSyntax)i.Accept(this)));
      block.Statements.AddRange(initializers);

      LuaExpressionSyntax condition = node.Condition != null ? (LuaExpressionSyntax)node.Condition.Accept(this) : LuaIdentifierNameSyntax.True;
      LuaWhileStatementSyntax whileStatement = new LuaWhileStatementSyntax(condition);
      blocks_.Push(whileStatement.Body);
      VisitLoopBody(node.Statement, whileStatement.Body);
      var incrementors = node.Incrementors.Select(i => new LuaExpressionStatementSyntax((LuaExpressionSyntax)i.Accept(this)));
      whileStatement.Body.Statements.AddRange(incrementors);
      blocks_.Pop();
      block.Statements.Add(whileStatement);
      blocks_.Pop();

      return block;
    }

    public override LuaSyntaxNode VisitDoStatement(DoStatementSyntax node) {
      var condition = (LuaExpressionSyntax)node.Condition.Accept(this);
      var newCondition = new LuaPrefixUnaryExpressionSyntax(new LuaParenthesizedExpressionSyntax(condition), LuaSyntaxNode.Tokens.Not);
      LuaRepeatStatementSyntax repeatStatement = new LuaRepeatStatementSyntax(newCondition);
      VisitLoopBody(node.Statement, repeatStatement.Body);
      return repeatStatement;
    }

    public override LuaSyntaxNode VisitYieldStatement(YieldStatementSyntax node) {
      CurFunction.HasYield = true;
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      if (node.IsKind(SyntaxKind.YieldBreakStatement)) {
        LuaReturnStatementSyntax returnStatement = new LuaReturnStatementSyntax(expression);
        return returnStatement;
      } else {
        LuaInvocationExpressionSyntax invocationExpression = new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.YieldReturn);
        invocationExpression.AddArgument(expression);
        return new LuaExpressionStatementSyntax(invocationExpression);
      }
    }

    public override LuaSyntaxNode VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) {
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);
      return new LuaParenthesizedExpressionSyntax(expression);
    }

    /// <summary>
    /// http://lua-users.org/wiki/TernaryOperator
    /// </summary>
    public override LuaSyntaxNode VisitConditionalExpression(ConditionalExpressionSyntax node) {
      bool mayBeNullOrFalse = MayBeNullOrFalse(node.WhenTrue);
      if (mayBeNullOrFalse) {
        var temp = GetTempIdentifier(node);
        var condition = (LuaExpressionSyntax)node.Condition.Accept(this);
        LuaIfStatementSyntax ifStatement = new LuaIfStatementSyntax(condition);
        blocks_.Push(ifStatement.Body);
        var whenTrue = (LuaExpressionSyntax)node.WhenTrue.Accept(this);
        blocks_.Pop();
        ifStatement.Body.Statements.Add(new LuaExpressionStatementSyntax(new LuaAssignmentExpressionSyntax(temp, whenTrue)));

        LuaElseClauseSyntax elseClause = new LuaElseClauseSyntax();
        blocks_.Push(elseClause.Body);
        var whenFalse = (LuaExpressionSyntax)node.WhenFalse.Accept(this);
        blocks_.Pop();
        elseClause.Body.Statements.Add(new LuaExpressionStatementSyntax(new LuaAssignmentExpressionSyntax(temp, whenFalse)));

        ifStatement.Else = elseClause;
        CurBlock.Statements.Add(new LuaLocalVariableDeclaratorSyntax(new LuaVariableDeclaratorSyntax(temp)));
        CurBlock.Statements.Add(ifStatement);
        return temp;
      } else {
        var condition = (LuaExpressionSyntax)node.Condition.Accept(this);
        var whenTrue = (LuaExpressionSyntax)node.WhenTrue.Accept(this);
        var whenFalse = (LuaExpressionSyntax)node.WhenFalse.Accept(this);
        return new LuaBinaryExpressionSyntax(new LuaBinaryExpressionSyntax(condition, LuaSyntaxNode.Tokens.And, whenTrue), LuaSyntaxNode.Tokens.Or, whenFalse);
      }
    }

    public override LuaSyntaxNode VisitGotoStatement(GotoStatementSyntax node) {
      if (node.CaseOrDefaultKeyword.IsKind(SyntaxKind.CaseKeyword)) {
        const string kCaseLabel = "caseLabel";
        var switchStatement = switchs_.Peek();
        int caseIndex = GetCaseLabelIndex(node);
        var labelIdentifier = switchStatement.CaseLabels.GetOrDefault(caseIndex);
        if (labelIdentifier == null) {
          string uniqueName = GetUniqueIdentifier(kCaseLabel + caseIndex, node);
          labelIdentifier = new LuaIdentifierNameSyntax(uniqueName);
          switchStatement.CaseLabels.Add(caseIndex, labelIdentifier);
        }
        return new LuaGotoCaseAdapterStatement(labelIdentifier);
      } else if (node.CaseOrDefaultKeyword.IsKind(SyntaxKind.DefaultKeyword)) {
        const string kDefaultLabel = "defaultLabel";
        var switchStatement = switchs_.Peek();
        if (switchStatement.DefaultLabel == null) {
          string identifier = GetUniqueIdentifier(kDefaultLabel, node);
          switchStatement.DefaultLabel = new LuaIdentifierNameSyntax(identifier);
        }
        return new LuaGotoCaseAdapterStatement(switchStatement.DefaultLabel);
      } else {
        var identifier = (LuaIdentifierNameSyntax)node.Expression.Accept(this);
        return new LuaGotoStatement(identifier);
      }
    }

    public override LuaSyntaxNode VisitLabeledStatement(LabeledStatementSyntax node) {
      LuaIdentifierNameSyntax identifier = new LuaIdentifierNameSyntax(node.Identifier.ValueText);
      var statement = (LuaStatementSyntax)node.Statement.Accept(this);
      return new LuaLabeledStatement(identifier, statement);
    }

    public override LuaSyntaxNode VisitEmptyStatement(EmptyStatementSyntax node) {
      return LuaStatementSyntax.Empty;
    }

    public override LuaSyntaxNode VisitCastExpression(CastExpressionSyntax node) {
      var constExpression = GetConstExpression(node);
      if (constExpression != null) {
        return constExpression;
      }

      var originalType = semanticModel_.GetTypeInfo(node.Expression).Type;
      var targetType = semanticModel_.GetTypeInfo(node.Type).Type;
      var expression = (LuaExpressionSyntax)node.Expression.Accept(this);

      if (targetType.IsAssignableFrom(originalType)) {
        return expression;
      }

      if (targetType.TypeKind == TypeKind.Enum) {
        if (originalType.TypeKind == TypeKind.Enum || originalType.IsIntegerType()) {
          return expression;
        }
      }

      if (targetType.IsIntegerType()) {
        if (originalType.TypeKind == TypeKind.Enum) {
          return expression;
        }

        if (originalType.IsIntegerType()) {
          return GetCastToNumberExpression(expression, targetType, false);
        }

        if (originalType.SpecialType == SpecialType.System_Double || originalType.SpecialType == SpecialType.System_Single) {
          return GetCastToNumberExpression(expression, targetType, true);
        }
      } else if (targetType.SpecialType == SpecialType.System_Single && originalType.SpecialType == SpecialType.System_Double) {
        return GetCastToNumberExpression(expression, targetType, true);
      }

      if (originalType.IsAssignableFrom(targetType)) {
        return BuildCastExpression(node.Type, expression);
      }

      var explicitSymbol = (IMethodSymbol)semanticModel_.GetSymbolInfo(node).Symbol;
      if (explicitSymbol != null) {
        return BuildConversionExpression(explicitSymbol, expression);
      }

      return BuildCastExpression(node.Type, expression);
    }

    private LuaExpressionSyntax BuildCastExpression(TypeSyntax type, LuaExpressionSyntax expression) {
      var typeExpression = (LuaExpressionSyntax)type.Accept(this);
      return new LuaInvocationExpressionSyntax(LuaIdentifierNameSyntax.Cast, typeExpression, expression);
    }

    private LuaExpressionSyntax GetCastToNumberExpression(LuaExpressionSyntax expression, ITypeSymbol targetType, bool isFromFloat) {
      string name = (isFromFloat ? "To" : "to") + targetType.Name;
      var methodName = new LuaMemberAccessExpressionSyntax(LuaIdentifierNameSyntax.System, new LuaIdentifierNameSyntax(name));
      return new LuaInvocationExpressionSyntax(methodName, expression);
    }

    public override LuaSyntaxNode VisitCheckedStatement(CheckedStatementSyntax node) {
      bool isChecked = node.Keyword.Kind() == SyntaxKind.CheckedKeyword;
      PushChecked(isChecked);
      LuaStatementListSyntax statements = new LuaStatementListSyntax();
      statements.Statements.Add(new LuaShortCommentStatement(" " + node.Keyword.ValueText));
      var block = (LuaStatementSyntax)node.Block.Accept(this);
      statements.Statements.Add(block);
      PopChecked();
      return statements;
    }

    public override LuaSyntaxNode VisitCheckedExpression(CheckedExpressionSyntax node) {
      bool isChecked = node.Keyword.Kind() == SyntaxKind.CheckedKeyword;
      PushChecked(isChecked);
      var expression = node.Expression.Accept(this);
      PopChecked();
      return expression;
    }
  }
}