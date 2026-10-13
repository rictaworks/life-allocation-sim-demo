using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace DomainCore.Tests.Support
{
    /// <summary>
    /// コンパイル済みのアセンブリで、禁止した型がどこで使われているかを調べる。
    /// 調べる場所：基底型・インターフェイス・フィールド・プロパティ・メソッドの引数と戻り値・ローカル変数・
    /// IL の命令が参照するメンバー（呼び出し先の宣言型・引数・戻り値、フィールド、型）。
    /// 禁止した IL 命令（浮動小数点の定数・変換など）も見つける。
    /// ソースの字句の検査では見えない使い方（int を暗黙に double へ変換して Math.Pow を呼ぶなど）を見つけるために使う。
    /// 解釈できない IL は例外にする（検査を素通りさせない）。
    /// </summary>
    internal sealed class TypeUsageInspector
    {
        private const BindingFlags DeclaredMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private const byte TwoByteOpCodePrefix = 0xFE;
        private const int TwoByteOpCodeBase = 0xFE00;
        private const int TokenSize = 4;

        private readonly Func<Type, bool> isForbidden;
        private readonly HashSet<short> forbiddenOpCodes;
        private readonly Dictionary<short, OpCode> opCodes;
        private readonly List<string> findings = new List<string>();

        private TypeUsageInspector(Func<Type, bool> isForbidden, IEnumerable<OpCode> forbiddenOpCodes)
        {
            this.isForbidden = isForbidden;
            this.forbiddenOpCodes = new HashSet<short>(forbiddenOpCodes.Select(opCode => opCode.Value));
            opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (OpCode)field.GetValue(null)!)
                .ToDictionary(opCode => opCode.Value);
        }

        public static IReadOnlyList<string> FindInAssembly(Assembly assembly, Func<Type, bool> isForbidden, IEnumerable<OpCode> forbiddenOpCodes)
        {
            var inspector = new TypeUsageInspector(isForbidden, forbiddenOpCodes);
            foreach (var type in assembly.GetTypes())
            {
                inspector.InspectType(type);
            }

            return inspector.findings;
        }

        public static IReadOnlyList<string> FindInMethod(MethodBase method, Func<Type, bool> isForbidden, IEnumerable<OpCode> forbiddenOpCodes)
        {
            var inspector = new TypeUsageInspector(isForbidden, forbiddenOpCodes);
            inspector.InspectMethod(method);
            return inspector.findings;
        }

        private static string NameOf(Type? type) => type?.FullName ?? type?.Name ?? string.Empty;

        private static IEnumerable<Type?> TypesReferencedBy(MemberInfo member)
        {
            switch (member)
            {
                case Type type:
                    return new[] { type };
                case FieldInfo field:
                    return new[] { field.FieldType, field.DeclaringType };
                case MethodBase method:
                    var types = new List<Type?> { method.DeclaringType };
                    types.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
                    if (method is MethodInfo info)
                    {
                        types.Add(info.ReturnType);
                    }

                    if (method.IsGenericMethod)
                    {
                        types.AddRange(method.GetGenericArguments());
                    }

                    return types;
                default:
                    throw new InvalidOperationException($"調べられないメンバーです: {member}");
            }
        }

        private void InspectType(Type type)
        {
            var where = NameOf(type);
            Check(type.BaseType, $"{where}: 基底型");
            foreach (var implemented in type.GetInterfaces())
            {
                Check(implemented, $"{where}: インターフェイス");
            }

            foreach (var field in type.GetFields(DeclaredMembers))
            {
                Check(field.FieldType, $"{where}.{field.Name}: フィールド");
            }

            foreach (var property in type.GetProperties(DeclaredMembers))
            {
                Check(property.PropertyType, $"{where}.{property.Name}: プロパティ");
            }

            var methods = new HashSet<MethodBase>(type.GetMethods(DeclaredMembers));
            methods.UnionWith(type.GetConstructors(DeclaredMembers));
            if (type.TypeInitializer != null)
            {
                methods.Add(type.TypeInitializer);
            }

            foreach (var method in methods)
            {
                InspectMethod(method);
            }
        }

        private void InspectMethod(MethodBase method)
        {
            var where = $"{NameOf(method.DeclaringType)}.{method.Name}";
            foreach (var parameter in method.GetParameters())
            {
                Check(parameter.ParameterType, $"{where}: 引数 {parameter.Name}");
            }

            if (method is MethodInfo info)
            {
                Check(info.ReturnType, $"{where}: 戻り値");
            }

            var body = method.GetMethodBody();
            if (body == null)
            {
                return;
            }

            foreach (var local in body.LocalVariables)
            {
                Check(local.LocalType, $"{where}: ローカル変数 {local.LocalIndex}");
            }

            var il = body.GetILAsByteArray();
            if (il == null)
            {
                return;
            }

            var typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            foreach (var instruction in Decode(il))
            {
                var at = $"{where}: IL_{instruction.Offset:X4} {instruction.OpCode.Name}";
                if (forbiddenOpCodes.Contains(instruction.OpCode.Value))
                {
                    findings.Add($"{at}（禁止した命令）");
                }

                if (instruction.OpCode.OperandType == OperandType.InlineSig)
                {
                    findings.Add($"{at}（calli の呼び出し先は調べられません）");
                }

                if (instruction.MemberToken.HasValue)
                {
                    var member = method.Module.ResolveMember(instruction.MemberToken.Value, typeArguments, methodArguments)
                        ?? throw new InvalidOperationException($"{at}: メンバーを解決できません");
                    foreach (var referenced in TypesReferencedBy(member))
                    {
                        Check(referenced, $"{at} {member}");
                    }
                }
            }
        }

        private void Check(Type? type, string where)
        {
            if (type != null && Involves(type))
            {
                findings.Add($"{where}（{type}）");
            }
        }

        private bool Involves(Type type)
        {
            if (type.HasElementType)
            {
                return Involves(type.GetElementType()!);
            }

            if (isForbidden(type))
            {
                return true;
            }

            return type.IsGenericType && type.GetGenericArguments().Any(Involves);
        }

        private IEnumerable<IlInstruction> Decode(byte[] il)
        {
            var offset = 0;
            while (offset < il.Length)
            {
                var start = offset;
                short value = il[offset++];
                if (value == TwoByteOpCodePrefix)
                {
                    if (offset >= il.Length)
                    {
                        throw new InvalidOperationException($"IL が途中で終わっています（IL_{start:X4}）");
                    }

                    value = unchecked((short)(TwoByteOpCodeBase | il[offset++]));
                }

                if (!opCodes.TryGetValue(value, out var opCode))
                {
                    throw new InvalidOperationException($"解釈できない IL 命令です: 0x{value:X4}（IL_{start:X4}）");
                }

                int? memberToken = null;
                switch (opCode.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        offset += 1;
                        break;
                    case OperandType.InlineVar:
                        offset += 2;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.ShortInlineR:
                        offset += 4;
                        break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                        memberToken = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, TokenSize));
                        offset += TokenSize;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        offset += 8;
                        break;
                    case OperandType.InlineSwitch:
                        var count = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, TokenSize));
                        offset += TokenSize + (TokenSize * count);
                        break;
                    default:
                        throw new InvalidOperationException($"解釈できないオペランドです: {opCode.OperandType}（IL_{start:X4}）");
                }

                yield return new IlInstruction(opCode, start, memberToken);
            }
        }

        private sealed class IlInstruction
        {
            public IlInstruction(OpCode opCode, int offset, int? memberToken)
            {
                OpCode = opCode;
                Offset = offset;
                MemberToken = memberToken;
            }

            public OpCode OpCode { get; }

            public int Offset { get; }

            public int? MemberToken { get; }
        }
    }
}
