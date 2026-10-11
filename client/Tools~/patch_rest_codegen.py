#!/usr/bin/env python3
"""Post-codegen fixes for client/Assets/Generated/Rest (openapi-generator 7.25.0, csharp, models only).

Run after `pnpm contract:codegen:rest`:  python3 client/Tools~/patch_rest_codegen.py

1. `type: boolean, const: true` schemas are emitted as `enum XEnum { True = true }`, which is not valid C#
   (enum values must be integral). Rewrites them to `True = 1` so the assembly compiles. The client never
   serializes these enums: it sends `ageConfirmed` as a JSON boolean through a raw body, and parses AdCreative
   raw. Root fix requested from backend in docs/progress/requests.md.
3. `*Cents` (and BoardEntry.value) generated as int32 are widened to long; see the comment below.
4. OpenAPI 3.1 dropped `nullable: true`, and generator 7.25.0 ignores it there, so nullable value types come out
   non-nullable. NULLABLE_VALUES lists the ones the client reads typed; they become `T?` so a JSON null stays unknown
   instead of reading as DateTime.MinValue / 0.
2. The codegen must run with `validatable=false` (System.ComponentModel.DataAnnotations is not in Unity's
   .NET Standard 2.1 profile). That leaves `class X : AbstractOpenAPISchema, ` on anyOf wrappers; the comma is removed.
"""
import pathlib, re, sys

root = pathlib.Path(__file__).resolve().parents[1] / "Assets" / "Generated" / "Rest"
pattern = re.compile(r"(\n\s*True\s*=\s*)true(\s*\n)")
# 3. Money is "bigint cents" per the contract, but `type: integer` without `format: int64` generates C# int (Int32):
#    balances above $21,474,836.47 would throw on deserialization. Widen every *Cents member (and board values) to long.
int_cents = re.compile(r"\bint (\w*Cents)\b")
int_cents_prop = re.compile(r"\bpublic int ([A-Z]\w*Cents)\b")
board_value = (re.compile(r"\bint value = default"), re.compile(r"\bpublic int Value \{"))

# 2. With validatable=false, anyOf/oneOf wrappers keep a dangling comma: `class X : AbstractOpenAPISchema, \n{`.
dangling = re.compile(r"(: AbstractOpenAPISchema),\s*\n")
# 4. (file, C# type, property): GET /me's instalment fields (backend eeef49d), null with no debt.
NULLABLE_VALUES = [("Me.cs", "DateTime", "DebtDueAt"), ("Me.cs", "long", "DebtWeeklyDueCents")]


def nullable_values(name, t):
    for file, kind, prop in NULLABLE_VALUES:
        if file != name:
            continue
        param = prop[0].lower() + prop[1:]
        t = re.sub(r"\b" + kind + r" " + param + r" = default\b", kind + "? " + param + " = default", t)
        t = re.sub(r"\bpublic " + kind + r" " + prop + r" \{", "public " + kind + "? " + prop + " {", t)
    return t


changed = 0
for f in root.rglob("*.cs"):
    s = f.read_text()
    t = pattern.sub(r"\g<1>1\2", s)
    t = dangling.sub(r"\1\n", t)
    t = int_cents.sub(r"long \1", t)
    t = int_cents_prop.sub(r"public long \1", t)
    t = nullable_values(f.name, t)
    if f.name in ("BoardEntry.cs",):
        t = board_value[0].sub("long value = default", t)
        t = board_value[1].sub("public long Value {", t)
    if t != s:
        f.write_text(t)
        changed += 1
        print("patched", f.relative_to(root))
print(f"{changed} file(s) patched")
sys.exit(0)
