# Shell Command Notes

## dotnet — chain commands
`&&` does NOT work in Windows cmd, but works fine in bash (which Claude Code uses here). Use `&&` normally.

## Avalonia — compiled bindings in DataTemplates
`x:DataType` on a `DataTemplate` or `TreeDataTemplate` inside a control's `DataTemplates` collection does NOT correctly scope compiled bindings — Avalonia resolves `{Binding X}` against the outer control's `x:DataType` instead of the template's type, returning null silently.

**Fix:** add `x:CompileBindings="False"` to each such template. This overrides the project-level `AvaloniaUseCompiledBindingsByDefault=true` for that scope and uses runtime reflection binding, which correctly uses the item's actual runtime type.
