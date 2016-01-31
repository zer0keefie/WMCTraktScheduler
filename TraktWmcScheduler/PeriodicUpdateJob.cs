using Quartz;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TraktWmcScheduler
{
    internal class PeriodicUpdateJob : IJob
    {
        public const string ControllerDataKey = "controller";

        #region IJob Members

        public void Execute(IJobExecutionContext context)
        {
            var controller = (SchedulerController)context.JobDetail.JobDataMap.Get(ControllerDataKey);
            bool result = false;

            try
            {
                result = controller.DoAutoUpdate();
            }
            catch
            {
            }

            context.Result = result;
        }

        #endregion
    }
}
