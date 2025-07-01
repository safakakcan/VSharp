using System.Collections.Generic;
namespace VSharp.Scripts;

public class VMethod : VNode
{
    public int ReturnPortIndex;
    public int[] ParameterPortIndices;
    public int[] BodyNodeIndices;
}
