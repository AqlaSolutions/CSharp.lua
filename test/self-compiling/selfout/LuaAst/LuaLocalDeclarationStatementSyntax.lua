-- Generated by CSharp.lua Compiler 1.0.0.0
local System = System
local CSharpLuaLuaAst
System.usingDeclare(function (global) 
    CSharpLuaLuaAst = CSharpLua.LuaAst
end)
System.namespace("CSharpLua.LuaAst", function (namespace) 
    namespace.class("LuaLocalVariablesStatementSyntax", function (namespace) 
        local getLocalKeyword, Render, __ctor__
        getLocalKeyword = function (this) 
            return "local" --[[Keyword.Local]]
        end
        Render = function (this, renderer) 
            renderer:Render20(this)
        end
        __ctor__ = function (this) 
            this.Variables = CSharpLuaLuaAst.LuaSyntaxList_1(CSharpLuaLuaAst.LuaIdentifierNameSyntax)()
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaVariableDeclarationSyntax
                }
            end, 
            getLocalKeyword = getLocalKeyword, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaEqualsValueClauseListSyntax", function (namespace) 
        local getEqualsToken, Render, __ctor__
        getEqualsToken = function (this) 
            return "=" --[[Tokens.Equals]]
        end
        Render = function (this, renderer) 
            renderer:Render21(this)
        end
        __ctor__ = function (this) 
            this.Values = CSharpLuaLuaAst.LuaSyntaxList_1(CSharpLuaLuaAst.LuaExpressionSyntax)()
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaSyntaxNode
                }
            end, 
            getEqualsToken = getEqualsToken, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaLocalDeclarationStatementSyntax", function (namespace) 
        local Render, __ctor__
        Render = function (this, renderer) 
            renderer:Render34(this)
        end
        __ctor__ = function (this, declaration) 
            CSharpLuaLuaAst.LuaStatementSyntax.__ctor__[1](this)
            this.Declaration = declaration
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaStatementSyntax
                }
            end, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaVariableDeclarationSyntax", function (namespace) 
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaStatementSyntax
                }
            end
        }
    end)
    namespace.class("LuaVariableListDeclarationSyntax", function (namespace) 
        local Render, __ctor__
        Render = function (this, renderer) 
            renderer:Render35(this)
        end
        __ctor__ = function (this) 
            this.Variables = CSharpLuaLuaAst.LuaSyntaxList_1(CSharpLuaLuaAst.LuaVariableDeclaratorSyntax)()
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaVariableDeclarationSyntax
                }
            end, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaLocalVariableDeclaratorSyntax", function (namespace) 
        local Render, __ctor1__, __ctor2__
        Render = function (this, renderer) 
            renderer:Render37(this)
        end
        __ctor1__ = function (this, declarator) 
            CSharpLuaLuaAst.LuaStatementSyntax.__ctor__[1](this)
            if declarator == nil then
                System.throw(System.ArgumentNullException("declarator"))
            end
            this.Declarator = declarator
        end
        __ctor2__ = function (this, identifier, expression) 
            CSharpLuaLuaAst.LuaStatementSyntax.__ctor__[1](this)
            this.Declarator = CSharpLuaLuaAst.LuaVariableDeclaratorSyntax(identifier, expression)
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaStatementSyntax
                }
            end, 
            Render = Render, 
            __ctor__ = {
                __ctor1__, 
                __ctor2__
            }
        }
    end)
    namespace.class("LuaVariableDeclaratorSyntax", function (namespace) 
        local getLocalKeyword, Render, __ctor__
        getLocalKeyword = function (this) 
            return "local" --[[Keyword.Local]]
        end
        Render = function (this, renderer) 
            renderer:Render36(this)
        end
        __ctor__ = function (this, identifier, expression) 
            CSharpLuaLuaAst.LuaStatementSyntax.__ctor__[1](this)
            if identifier == nil then
                System.throw(System.ArgumentNullException("identifier"))
            end
            this.Identifier = identifier
            if expression ~= nil then
                this.Initializer = CSharpLuaLuaAst.LuaEqualsValueClauseSyntax(expression)
            end
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaStatementSyntax
                }
            end, 
            getLocalKeyword = getLocalKeyword, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaEqualsValueClauseSyntax", function (namespace) 
        local getEqualsToken, Render, __ctor__
        getEqualsToken = function (this) 
            return "=" --[[Tokens.Equals]]
        end
        Render = function (this, renderer) 
            renderer:Render33(this)
        end
        __ctor__ = function (this, value) 
            CSharpLuaLuaAst.LuaSyntaxNode.__ctor__[1](this)
            if value == nil then
                System.throw(System.ArgumentNullException("value"))
            end
            this.Value = value
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaSyntaxNode
                }
            end, 
            getEqualsToken = getEqualsToken, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
    namespace.class("LuaTypeLocalAreaSyntax", function (namespace) 
        local getLocalKeyword, Render, __ctor__
        getLocalKeyword = function (this) 
            return "local" --[[Keyword.Local]]
        end
        Render = function (this, renderer) 
            renderer:Render38(this)
        end
        __ctor__ = function (this) 
            this.Variables = CSharpLuaLuaAst.LuaSyntaxList_1(CSharpLuaLuaAst.LuaIdentifierNameSyntax)()
        end
        return {
            __inherits__ = function () 
                return {
                    CSharpLuaLuaAst.LuaStatementSyntax
                }
            end, 
            getLocalKeyword = getLocalKeyword, 
            Render = Render, 
            __ctor__ = __ctor__
        }
    end)
end)
