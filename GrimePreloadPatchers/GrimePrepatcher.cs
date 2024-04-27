using BepInEx.Preloader.Core.Patching;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GrimePreloadPatchers
{
    [PatcherPluginInfo("com.badmagic.grimepreloaderplugin", "Grime Preloader Patchers", "0.1.0")]
    public class GrimePrepatcher : BasePatcher
    {
        [TargetType("Rewired_Core.dll", "Rewired.Utils.Classes.Data.ADictionary`2")]
        public void PatchADictionary(TypeDefinition type)
        {
            PatchDictionaryType(type);
        }
        
        [TargetType("Rewired_Core.dll", "Rewired.Utils.Classes.Data.IndexedDictionary`2")]
        public void PatchIndexedDictionary(TypeDefinition type)
        {
            PatchDictionaryType(type);
        }

        private void PatchDictionaryType(TypeDefinition type)
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

        private void CreateShimIntLikeProperty(TypeDefinition typeToModify, PropertyDefinition implementing)
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

        private void CreateWrapperProperty(
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

        private MethodAttributes BuildAttrs()
        {
            return MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.Virtual 
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName;
        }

        private MethodReference CloneMethodReferenceGeneric(MethodReference method, params TypeReference[] arguments)
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
