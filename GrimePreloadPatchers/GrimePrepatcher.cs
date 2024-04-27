using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GrimePreloadPatchers
{
    public static class Patcher
    {
        private static readonly HashSet<string> dictionaryTypes = [
            "Rewired.Utils.Classes.Data.ADictionary`2",
            "Rewired.Utils.Classes.Data.IndexedDictionary`2"
        ];

        public static IEnumerable<string> TargetDLLs => ["Rewired_Core.dll"];

        public static void Patch(AssemblyDefinition assembly)
        {
            foreach (ModuleDefinition module in assembly.Modules)
            {
                foreach (TypeDefinition type in module.Types)
                {
                    if (dictionaryTypes.Contains(type.FullName))
                    {
                        PatchDictionaryType(type);
                    }
                }
            }
        }

        private static void PatchDictionaryType(TypeDefinition type)
        {
            TypeReference _iCollection = type.Module.ImportReference(typeof(ICollection<>));

            TypeDefinition _iDictionary = type.Module.ImportReference(typeof(IDictionary)).Resolve();
            // resolve will drop all the generic params, no point to make a generic instance of this
            TypeDefinition _iDictionaryTkeyTValue = type.Module.ImportReference(typeof(IDictionary<,>))
                .Resolve();

            PropertyDefinition keysProp = type.Properties.First(p => p.Name == "System.Collections.IDictionary.Keys");
            TypeReference _keyCollection = _iCollection.MakeGenericInstanceType(type.GenericParameters.First());

            CreateShimIntLikeProperty(type, _iDictionary.Properties.First(p => p.Name == "IsFixedSize"));
            CreateShimIntLikeProperty(type, _iDictionary.Properties.First(p => p.Name == "IsReadOnly"));

            CreateWrapperProperty(
                "System.Collections.Generic.IDictionary<TKey,TValue>.",
                type,
                _iDictionaryTkeyTValue.Properties.First(p => p.Name == "Keys"),
                keysProp,
                _keyCollection);
        }

        private static void CreateShimIntLikeProperty(TypeDefinition typeToModify, PropertyDefinition implementing)
        {
            string prefix = implementing.DeclaringType.FullName + ".";
            MethodDefinition getter = new(prefix + implementing.GetMethod.Name, BuildAttrs(), implementing.PropertyType);
            ILProcessor proc = getter.Body.GetILProcessor();
            proc.Append(proc.Create(OpCodes.Ldc_I4_0));
            proc.Append(proc.Create(OpCodes.Ret));
            getter.Overrides.Add(typeToModify.Module.ImportReference(implementing.GetMethod));
            typeToModify.Methods.Add(getter);

            PropertyDefinition prop = new(prefix + implementing.Name, PropertyAttributes.None, implementing.PropertyType)
            {
                GetMethod = getter
            };
            typeToModify.Properties.Add(prop);
        }

        private static void CreateWrapperProperty(
            string prefix,
            TypeDefinition typeToModify,
            PropertyDefinition implementing,
            PropertyDefinition propToWrap,
            TypeReference propType)
        {
            MethodDefinition getter = new(prefix + implementing.GetMethod.Name, BuildAttrs(), propType);
            ILProcessor proc = getter.Body.GetILProcessor();
            proc.Append(proc.Create(OpCodes.Ldarg_0));
            proc.Append(proc.Create(OpCodes.Callvirt, propToWrap.GetMethod));
            proc.Append(proc.Create(OpCodes.Ret));
            MethodReference overriden = typeToModify.Module.ImportReference(implementing.GetMethod);
            overriden = CloneMethodReferenceGeneric(overriden, typeToModify.GenericParameters.ElementAt(0), typeToModify.GenericParameters.ElementAt(1));
            getter.Overrides.Add(overriden);
            typeToModify.Methods.Add(getter);

            PropertyDefinition prop = new(prefix + implementing.Name, PropertyAttributes.None, propType)
            {
                GetMethod = getter
            };
            typeToModify.Properties.Add(prop);
        }

        private static MethodAttributes BuildAttrs()
        {
            return MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.Virtual 
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName;
        }

        private static MethodReference CloneMethodReferenceGeneric(MethodReference method, params TypeReference[] arguments)
        {
            MethodReference clone = new MethodReference(method.Name, method.ReturnType, method.DeclaringType.MakeGenericInstanceType(arguments))
            {
                HasThis = method.HasThis,
                ExplicitThis = method.ExplicitThis,
                CallingConvention = method.CallingConvention,
            };
            foreach (ParameterReference p in method.Parameters)
            {
                clone.Parameters.Add(new ParameterDefinition(p.ParameterType));
            }
            foreach (GenericParameter g in method.GenericParameters)
            {
                clone.GenericParameters.Add(new GenericParameter(g.Name, clone));
            }
            return clone;
        }
    }
}
