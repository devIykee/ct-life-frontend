#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !CTLIFE_PROD
using System;
namespace CTLife.App.Review
{
    internal static class HomeTouchTarget
    {
        internal static bool Meets(double width,double height,double minHeight,double scale)
        {
            if(double.IsNaN(scale)||double.IsInfinity(scale)||scale<=0||!(minHeight>=124))return false;
            double minimumPixels=Math.Floor(124*scale);
            const double numericalEpsilonPixels=0.001;
            return width*scale+numericalEpsilonPixels>=minimumPixels&&height*scale+numericalEpsilonPixels>=minimumPixels;
        }
    }
}
#endif
