// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.md in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNet.SignalR.ServiceBus
{
    public static class HashCode
    {
        public static int Combine(int hashCode1, int hashCode2)
        {
            return ((hashCode1 << 5) + hashCode1) ^ hashCode2;
        }

        [SuppressMessage("Microsoft.Design", "CA1025:ReplaceRepetitiveArgumentsWithParamsArray", Justification = "Done for efficiency")]
        public static int Combine(int hashCode1, int hashCode2, int hashCode3, int hashCode4)
        {
            return Combine(Combine(hashCode1, hashCode2), Combine(hashCode3, hashCode4));
        }

        [SuppressMessage("Microsoft.Design", "CA1025:ReplaceRepetitiveArgumentsWithParamsArray", Justification = "Done for efficiency")]
        public static int Combine(int hashCode1, int hashCode2, int hashCode3, int hashCode4, int hashCode5, int hashCode6, int hashCode7, int hashCode8)
        {
            return Combine(Combine(hashCode1, hashCode2, hashCode3, hashCode4), Combine(hashCode5, hashCode6, hashCode7, hashCode8));
        }
    }
}
