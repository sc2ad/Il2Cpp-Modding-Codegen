﻿using Il2Cpp_Modding_Codegen.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2Cpp_Modding_Codegen.Serialization
{
    public class UnresolvedTypeException : Exception
    {
        public UnresolvedTypeException(TypeRef declaringType, TypeRef typeFailed) : base($"{declaringType} could not find reference to type: {typeFailed}")
        {
        }
    }
}