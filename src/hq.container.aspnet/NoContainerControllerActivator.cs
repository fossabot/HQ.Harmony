using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace hq.container.aspnet
{
    public sealed class NoContainerControllerActivator : IControllerActivator
    {
        private readonly IContainer _container;

        public NoContainerControllerActivator(IContainer container)
        {
            _container = container;
        }
        
        public object Create(ControllerContext context)
        {
            return _container.Resolve(context.ActionDescriptor.ControllerTypeInfo.AsType());
        }

        public void Release(ControllerContext context, object controller)
        {
            // Nothing to do; container manages instance lifetimes
        }
    }
}