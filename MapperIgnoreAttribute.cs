using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    //特性类需要继承自Attribute类，并且可以通过AttributeUsage特性来指定该特性的使用范围和是否允许多次使用。


    [AttributeUsage(AttributeTargets.Property,AllowMultiple =false)]
    public class MapperIgnoreAttribute:Attribute
    {
    }
}
