using System;

namespace ColorlessCore.Inspector
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class ExpandableAttribute : DrawerAttribute
    {
        public bool IsReadonly { get; private set; }

        public ExpandableAttribute(bool isReadonly = false)
        {
            IsReadonly = isReadonly;
        }
    }
}
