-- Generated by CSharp.lua Compiler 1.0.0.0
local System = System
local Linq = System.Linq.Enumerable
local MicrosoftCodeAnalysis = Microsoft.CodeAnalysis
local MicrosoftCodeAnalysisCSharp = Microsoft.CodeAnalysis.CSharp
local MicrosoftCodeAnalysisCSharpSyntax = Microsoft.CodeAnalysis.CSharp.Syntax
local SystemIO = System.IO
local SystemLinq = System.Linq
local CSharpLua
local CSharpLuaLuaAst
System.usingDeclare(function (global) 
    CSharpLua = global.CSharpLua
    CSharpLuaLuaAst = CSharpLua.LuaAst
end)
System.namespace("CSharpLua", function (namespace) 
    namespace.class("CmdArgumentException", function (namespace) 
        local __ctor__
        __ctor__ = function (this, message) 
            System.Exception.__ctor__(this, message)
        end
        return {
            __inherits__ = function () 
                return {
                    System.Exception
                }
            end, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("CompilationErrorException", function (namespace) 
        local __ctor__
        __ctor__ = function (this, message) 
            System.Exception.__ctor__(this, message)
        end
        return {
            __inherits__ = function () 
                return {
                    System.Exception
                }
            end, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("Utility", function (namespace) 
        local GetCommondLines, First, Last, GetOrDefault, GetOrDefault1, AddAt, IndexOf, GetArgument, 
        GetCurrentDirectory, Split, IsPrivate, IsPrivate1, IsStatic, IsAbstract, IsReadOnly, IsConst, 
        IsParams, IsPartial, IsOutOrRef, IsStringType, IsDelegateType, IsIntegerType, IsImmutable, IsInterfaceImplementation, 
        InterfaceImplementations, IsFromCode, IsOverridable, OverriddenSymbol, IsOverridden, IsPropertyField, IsEventFiled, IsAssignment, 
        systemLinqEnumerableType_, IsSystemLinqEnumerable, GetLocationString, IsSubclassOf, IsImplementInterface, IsBaseNumberType, IsNumberTypeAssignableFrom, IsAssignableFrom, 
        CheckOriginalDefinition, CheckOriginalDefinition1
        GetCommondLines = function (args) 
            local cmds = System.Dictionary(System.String, System.Array(System.String))()

            local key = ""
            local values = System.List(System.String)()

            for _, arg in System.each(args) do
                local i = arg:Trim()
                if i:StartsWith("-") then
                    if not System.String.IsNullOrEmpty(key) then
                        cmds:Add(key, values:ToArray())
                        key = ""
                        values:Clear()
                    end
                    key = i
                else
                    values:Add(i)
                end
            end

            if not System.String.IsNullOrEmpty(key) then
                cmds:Add(key, values:ToArray())
            end
            return cmds
        end
        First = function (list, T) 
            return list:get(0)
        end
        Last = function (list, T) 
            return list:get(list:getCount() - 1)
        end
        GetOrDefault = function (list, index, v, T) 
            local default
            if index >= 0 and index < list:getCount() then
                default = list:get(index)
            else
                default = v
            end
            return default
        end
        GetOrDefault1 = function (dict, key, t, K, T) 
            local v
            local default
            default, v = dict:TryGetValue(key, v)
            if default then
                return v
            end
            return t
        end
        AddAt = function (list, index, v, T) 
            if index < list:getCount() then
                list:set(index, v)
            else
                local count = index - list:getCount()
                do
                    local i = 0
                    while i < count do
                        list:Add(System.default(T))
                        i = i + 1
                    end
                end
                list:Add(v)
            end
        end
        IndexOf = function (source, match, T) 
            local index = 0
            for _, item in System.each(source) do
                if match(item) then
                    return index
                end
                index = index + 1
            end
            return - 1
        end
        GetArgument = function (args, name, isOption) 
            local values = GetOrDefault1(args, name, nil, System.String, System.Array(System.String))
            if values == nil or #values == 0 then
                if isOption then
                    return nil
                end
                System.throw(CSharpLua.CmdArgumentException((name or "") .. " is not found"))
            end
            return values:get(0)
        end
        GetCurrentDirectory = function (path) 
            local CurrentDirectorySign1 = "~/"
            local CurrentDirectorySign2 = "~\\"

            if path:StartsWith(CurrentDirectorySign1) then
                return SystemIO.Path.Combine(System.AppDomain.getCurrentDomain():getBaseDirectory(), path:Substring(#CurrentDirectorySign1))
            elseif path:StartsWith(CurrentDirectorySign2) then
                return SystemIO.Path.Combine(System.AppDomain.getCurrentDomain():getBaseDirectory(), path:Substring(#CurrentDirectorySign2))
            end

            return SystemIO.Path.Combine(System.Environment.getCurrentDirectory(), path)
        end
        Split = function (s, isPath) 
            local list = System.HashSet(System.String)()
            if not System.String.IsNullOrEmpty(s) then
                local array = s:Split(59 --[[';']])
                for _, i in System.each(array) do
                    local default
                    if isPath then
                        default = GetCurrentDirectory(i)
                    else
                        default = i
                    end
                    list:Add(default)
                end
            end
            return Linq.ToArray(list)
        end
        IsPrivate = function (symbol) 
            return symbol:getDeclaredAccessibility() == 1 --[[Accessibility.Private]]
        end
        IsPrivate1 = function (modifiers) 
            for _, modifier in System.each(modifiers) do
                repeat
                    local default = MicrosoftCodeAnalysisCSharp.CSharpExtensions.Kind(modifier)
                    if default == 8344 --[[SyntaxKind.PrivateKeyword]] then
                        do
                            return true
                        end
                    elseif default == 8343 --[[SyntaxKind.PublicKeyword]] or default == 8345 --[[SyntaxKind.InternalKeyword]] or default == 8346 --[[SyntaxKind.ProtectedKeyword]] then
                        do
                            return false
                        end
                    end
                until 1
            end
            return true
        end
        IsStatic = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8347 --[[SyntaxKind.StaticKeyword]]) end)
        end
        IsAbstract = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8356 --[[SyntaxKind.AbstractKeyword]]) end)
        end
        IsReadOnly = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8348 --[[SyntaxKind.ReadOnlyKeyword]]) end)
        end
        IsConst = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8350 --[[SyntaxKind.ConstKeyword]]) end)
        end
        IsParams = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8365 --[[SyntaxKind.ParamsKeyword]]) end)
        end
        IsPartial = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8406 --[[SyntaxKind.PartialKeyword]]) end)
        end
        IsOutOrRef = function (modifiers) 
            return Linq.Any(modifiers, function (i) return MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8361 --[[SyntaxKind.OutKeyword]]) or MicrosoftCodeAnalysis.CSharpExtensions.IsKind(i, 8360 --[[SyntaxKind.RefKeyword]]) end)
        end
        IsStringType = function (type) 
            return type:getSpecialType() == 20 --[[SpecialType.System_String]]
        end
        IsDelegateType = function (type) 
            return type:getTypeKind() == 3 --[[TypeKind.Delegate]]
        end
        IsIntegerType = function (type) 
            return type:getSpecialType() >= 9 --[[SpecialType.System_SByte]] and type:getSpecialType() <= 16 --[[SpecialType.System_UInt64]]
        end
        IsImmutable = function (type) 
            local isImmutable = (type:getIsValueType() and type:getIsDefinition()) or IsStringType(type) or IsDelegateType(type)
            return isImmutable
        end
        IsInterfaceImplementation = function (symbol, T) 
            if not symbol:getIsStatic() then
                local type = symbol:getContainingType()
                if type ~= nil then
                    local interfaceSymbols = Linq.SelectMany(type:getAllInterfaces(), function (i) return i:GetMembers():OfType(T) end, T)
                    return Linq.Any(interfaceSymbols, function (i) return symbol:Equals(type:FindImplementationForInterfaceMember(i)) end)
                end
            end
            return false
        end
        InterfaceImplementations = function (symbol, T) 
            if not symbol:getIsStatic() then
                local type = symbol:getContainingType()
                if type ~= nil then
                    local interfaceSymbols = Linq.SelectMany(type:getAllInterfaces(), function (i) return i:GetMembers():OfType(T) end, T)
                    return Linq.Where(interfaceSymbols, function (i) return symbol:Equals(type:FindImplementationForInterfaceMember(i)) end)
                end
            end
            return System.Array.Empty(T)
        end
        IsFromCode = function (symbol) 
            return not symbol:getDeclaringSyntaxReferences():getIsEmpty()
        end
        IsOverridable = function (symbol) 
            return not symbol:getIsStatic() and (symbol:getIsAbstract() or symbol:getIsVirtual() or symbol:getIsOverride())
        end
        OverriddenSymbol = function (symbol) 
            repeat
                local default = symbol:getKind()
                if default == 9 --[[SymbolKind.Method]] then
                    do
                        local methodSymbol = System.cast(MicrosoftCodeAnalysis.IMethodSymbol, symbol)
                        return methodSymbol:getOverriddenMethod()
                    end
                elseif default == 15 --[[SymbolKind.Property]] then
                    do
                        local propertySymbol = System.cast(MicrosoftCodeAnalysis.IPropertySymbol, symbol)
                        return propertySymbol:getOverriddenProperty()
                    end
                elseif default == 5 --[[SymbolKind.Event]] then
                    do
                        local eventSymbol = System.cast(MicrosoftCodeAnalysis.IEventSymbol, symbol)
                        return eventSymbol:getOverriddenEvent()
                    end
                end
            until 1
            return nil
        end
        IsOverridden = function (symbol, superSymbol) 
            while true do
                local overriddenSymbol = OverriddenSymbol(symbol)
                if overriddenSymbol ~= nil then
                    overriddenSymbol = CheckOriginalDefinition1(overriddenSymbol)
                    if overriddenSymbol:Equals(superSymbol) then
                        return true
                    end
                    symbol = overriddenSymbol
                else
                    return false
                end
            end
        end
        IsPropertyField = function (symbol) 
            if IsOverridable(symbol) then
                return false
            end

            local syntaxReference = SystemLinq.ImmutableArrayExtensions.FirstOrDefault(symbol:getDeclaringSyntaxReferences(), MicrosoftCodeAnalysis.SyntaxReference)
            if syntaxReference ~= nil then
                local node = System.cast(MicrosoftCodeAnalysisCSharpSyntax.PropertyDeclarationSyntax, syntaxReference:GetSyntax(nil))
                local hasGet = false
                local hasSet = false
                if node:getAccessorList() ~= nil then
                    for _, accessor in System.each(node:getAccessorList():getAccessors()) do
                        if accessor:getBody() ~= nil then
                            if MicrosoftCodeAnalysis.CSharpExtensions.IsKind(accessor, 8896 --[[SyntaxKind.GetAccessorDeclaration]]) then
                                assert(not hasGet)
                                hasGet = true
                            else
                                assert(not hasSet)
                                hasSet = true
                            end
                        end
                    end
                else
                    assert(not hasGet)
                    hasGet = true
                end
                local isAuto = not hasGet and not hasSet
                if isAuto then
                    if IsInterfaceImplementation(symbol, MicrosoftCodeAnalysis.IPropertySymbol) then
                        isAuto = false
                    end
                end
                return isAuto
            end
            return false
        end
        IsEventFiled = function (symbol) 
            if IsOverridable(symbol) then
                return false
            end

            local syntaxReference = SystemLinq.ImmutableArrayExtensions.FirstOrDefault(symbol:getDeclaringSyntaxReferences(), MicrosoftCodeAnalysis.SyntaxReference)
            if syntaxReference ~= nil then
                local isField = MicrosoftCodeAnalysis.CSharpExtensions.IsKind(syntaxReference:GetSyntax(nil), 8795 --[[SyntaxKind.VariableDeclarator]])
                if isField then
                    if IsInterfaceImplementation(symbol, MicrosoftCodeAnalysis.IEventSymbol) then
                        isField = false
                    end
                end
                return isField
            end
            return false
        end
        IsAssignment = function (kind) 
            return kind >= 8714 --[[SyntaxKind.SimpleAssignmentExpression]] and kind <= 8724 --[[SyntaxKind.RightShiftAssignmentExpression]]
        end
        IsSystemLinqEnumerable = function (symbol) 
            if systemLinqEnumerableType_ ~= nil then
                return symbol == systemLinqEnumerableType_
            else
                local success = symbol:ToString() == CSharpLuaLuaAst.LuaIdentifierNameSyntax.SystemLinqEnumerable.ValueText
                if success then
                    systemLinqEnumerableType_ = symbol
                end
                return success
            end
        end
        GetLocationString = function (node) 
            local location = node:getSyntaxTree():GetLocation(node:getSpan())
            local methodInfo = location:GetType():GetMethod("GetDebuggerDisplay", 4 --[[BindingFlags.Instance]] | 32 --[[BindingFlags.NonPublic]])
            return System.cast(System.String, methodInfo:Invoke(location, nil))
        end
        IsSubclassOf = function (child, parent) 
            local p = child
            if p == parent then
                return false
            end

            while p ~= nil do
                if p == parent then
                    return true
                end
                p = p:getBaseType()
            end
            return false
        end
        IsImplementInterface = function (implementType, interfaceType) 
            local t = implementType
            while t ~= nil do
                local interfaces = implementType:getAllInterfaces()
                for _, i in System.each(interfaces) do
                    if i == interfaceType or IsImplementInterface(i, interfaceType) then
                        return true
                    end
                end
                t = t:getBaseType()
            end
            return false
        end
        IsBaseNumberType = function (specialType) 
            return specialType >= 8 --[[SpecialType.System_Char]] and specialType <= 19 --[[SpecialType.System_Double]]
        end
        IsNumberTypeAssignableFrom = function (left, right) 
            if IsBaseNumberType(left:getSpecialType()) and IsBaseNumberType(right:getSpecialType()) then
                local begin
                repeat
                    local default = left:getSpecialType()
                    if default == 8 --[[SpecialType.System_Char]] or default == 9 --[[SpecialType.System_SByte]] or default == 10 --[[SpecialType.System_Byte]] then
                        do
                            begin = 11 --[[SpecialType.System_Int16]]
                            break
                        end
                    elseif default == 11 --[[SpecialType.System_Int16]] or default == 12 --[[SpecialType.System_UInt16]] then
                        do
                            begin = 13 --[[SpecialType.System_Int32]]
                            break
                        end
                    elseif default == 13 --[[SpecialType.System_Int32]] or default == 14 --[[SpecialType.System_UInt32]] then
                        do
                            begin = 15 --[[SpecialType.System_Int64]]
                            break
                        end
                    elseif default == 15 --[[SpecialType.System_Int64]] or default == 16 --[[SpecialType.System_UInt64]] then
                        do
                            begin = 17 --[[SpecialType.System_Decimal]]
                            break
                        end
                    else
                        do
                            begin = 19 --[[SpecialType.System_Double]]
                            break
                        end
                    end
                until 1
                local end_ = 19 --[[SpecialType.System_Double]]
                return left:getSpecialType() >= begin and left:getSpecialType() <= end_
            end
            return false
        end
        IsAssignableFrom = function (left, right) 
            if left == right then
                return true
            end

            if IsNumberTypeAssignableFrom(left, right) then
                return true
            end

            if IsSubclassOf(right, left) then
                return true
            end

            if left:getTypeKind() == 7 --[[TypeKind.Interface]] then
                return IsImplementInterface(right, left)
            end

            return false
        end
        CheckOriginalDefinition = function (symbol) 
            if symbol:getIsExtensionMethod() then
                if symbol:getReducedFrom() ~= nil then
                    symbol = symbol:getReducedFrom()
                end
            else
                if symbol:getOriginalDefinition() ~= symbol then
                    symbol = symbol:getOriginalDefinition()
                end
            end
            return symbol
        end
        CheckOriginalDefinition1 = function (symbol) 
            if symbol:getOriginalDefinition() ~= symbol then
                symbol = symbol:getOriginalDefinition()
            end
            return symbol
        end
        return {
            GetCommondLines = GetCommondLines, 
            First = First, 
            Last = Last, 
            GetOrDefault = GetOrDefault, 
            GetOrDefault1 = GetOrDefault1, 
            AddAt = AddAt, 
            IndexOf = IndexOf, 
            GetArgument = GetArgument, 
            GetCurrentDirectory = GetCurrentDirectory, 
            Split = Split, 
            IsPrivate = IsPrivate, 
            IsPrivate1 = IsPrivate1, 
            IsStatic = IsStatic, 
            IsAbstract = IsAbstract, 
            IsReadOnly = IsReadOnly, 
            IsConst = IsConst, 
            IsParams = IsParams, 
            IsPartial = IsPartial, 
            IsOutOrRef = IsOutOrRef, 
            IsStringType = IsStringType, 
            IsDelegateType = IsDelegateType, 
            IsIntegerType = IsIntegerType, 
            IsImmutable = IsImmutable, 
            IsInterfaceImplementation = IsInterfaceImplementation, 
            InterfaceImplementations = InterfaceImplementations, 
            IsFromCode = IsFromCode, 
            IsOverridable = IsOverridable, 
            OverriddenSymbol = OverriddenSymbol, 
            IsOverridden = IsOverridden, 
            IsPropertyField = IsPropertyField, 
            IsEventFiled = IsEventFiled, 
            IsAssignment = IsAssignment, 
            IsSystemLinqEnumerable = IsSystemLinqEnumerable, 
            GetLocationString = GetLocationString, 
            IsSubclassOf = IsSubclassOf, 
            IsAssignableFrom = IsAssignableFrom, 
            CheckOriginalDefinition = CheckOriginalDefinition, 
            CheckOriginalDefinition1 = CheckOriginalDefinition1
        }
    end)
end)
