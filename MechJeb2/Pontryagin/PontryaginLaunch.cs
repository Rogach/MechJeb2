using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MuMech {
    public class PontryaginLaunch : PontryaginBase {
        public PontryaginLaunch(double mu, Vector3d r0, Vector3d v0, Vector3d pv0, Vector3d pr0, double dV) : base(mu, r0, v0, pv0, pr0, dV)
        {
        }

        public override int arcIndex { get { return 3; } }

        double rTm;
        double vTm;
        double gamma;
        double inc;

        // 4-constraint PEG with free LAN
        public void flightangle4constraint(double rTm, double vTm, double gamma, double inc)
        {
            this.rTm = rTm;
            this.vTm = vTm;
            this.gamma = gamma;
            this.inc = inc;
            bcfun = flightangle4constraint;
        }

        private void flightangle4constraint(double[] yT, double[] z)
        {
            double rTm_bar = rTm / r_scale;
            double vTm_bar = vTm / v_scale;

            Vector3d rf = new Vector3d(yT[0], yT[1], yT[2]);
            Vector3d vf = new Vector3d(yT[3], yT[4], yT[5]);
            Vector3d pvf = new Vector3d(yT[6], yT[7], yT[8]);
            Vector3d prf = new Vector3d(yT[9], yT[10], yT[11]);

            Vector3d n = new Vector3d(0, -1, 0);  /* angular momentum vectors point south in KSP */
            Vector3d rn = Vector3d.Cross(rf, n);
            Vector3d vn = Vector3d.Cross(vf, n);
            Vector3d hf = Vector3d.Cross(rf, vf);

            z[0] = ( rf.magnitude * rf.magnitude - rTm_bar * rTm_bar ) / 2.0;
            z[1] = ( vf.magnitude * vf.magnitude - vTm_bar * vTm_bar ) / 2.0;
            z[2] = Vector3d.Dot(n, hf) - hf.magnitude * Math.Cos(inc);
            z[3] = Vector3d.Dot(rf, vf) - rf.magnitude * vf.magnitude * Math.Sin(gamma);
            z[4] = rTm_bar * rTm_bar * ( Vector3d.Dot(vf, prf) - vTm_bar * Math.Sin(gamma) / rTm_bar * Vector3d.Dot(rf, prf) ) -
                vTm_bar * vTm_bar * ( Vector3d.Dot(rf, pvf) - rTm_bar * Math.Sin(gamma) / vTm_bar * Vector3d.Dot(vf, pvf) );
            z[5] = Vector3d.Dot(hf, prf) * Vector3d.Dot(hf, rn) + Vector3d.Dot(hf, pvf) * Vector3d.Dot(hf, vn);
        }

        public override void optimizationFunction(double[] y0, double[] z, object o)
        {
            List<Arc> arcs = (List<Arc>)o;
            base.optimizationFunction(y0, z, o);

            /* magnitude of initial costate vector = 1.0 (dummy constraint for H(tf)=0 because BC is keplerian) */
            int n = 13 * arcs.Count;
            z[n] = 0.0;
            for(int i = 0; i < 6; i++)
                z[n] = z[n] + y0[6+i+arcIndex] * y0[6+i+arcIndex];
            z[n] = Math.Sqrt(z[n]) - 1.0;

            // positive arc time constraint
            z[n+1] = ( y0[0] < 0 ) ? ( y0[0] - y0[1] * y0[1] ) * 1000 : y0[1] * y0[1];

            z[n+2] = y0[2] * y0[2]; // FIXME: remove this if its unnecessary now

            /* construct sum of the squares of the residuals for levenberg marquardt */
            for(int i = 0; i < z.Length; i++)
                z[i] = z[i] * z[i];
        }

        public void Bootstrap()
        {
            y0 = new double[13 + arcIndex];
            double ve = g0 * arcs[0].isp;
            Debug.Log("dV = " + dV);
            tgo = ve * arcs[0].m0 / arcs[0].thrust * ( 1 - Math.Exp(-dV/ve) );
            tgo_bar = tgo / t_scale;
            Debug.Log("tgo = " + tgo);
            Debug.Log("tgo_bar = " + tgo_bar);
            UpdateY0Arc(0);

            for(int i = 1; i < arcs.Count; i++)
            {
                for(int j = 0; j < y0.Length; j++)
                    Debug.Log("  " + i + " y0[" + j + "] = " + y0[j]);

                List<Arc> subarcs = arcs.GetRange(0, i);
                runOptimizer(subarcs);  // FIXME: check return value

                Solution sol = new Solution(t_scale, v_scale, r_scale, 0);

                for(int j = 0; j < y0.Length; j++)
                    Debug.Log(i + " y0[" + j + "] = " + y0[j]);

                multipleIntegrate(y0, sol, subarcs, 10);

                double[] y0_new = new double[13 * (i + 1) + arcIndex];
                Array.Copy(y0, 0, y0_new, 0, 13*i + arcIndex);
                y0 = y0_new;

                double t = arcs[i-1].max_bt_bar;
                Vector3d r = sol.r_bar(t);
                Vector3d v = sol.v_bar(t);
                Vector3d pv = sol.pv_bar(t);
                Vector3d pr = sol.pr_bar(t);
                double m = sol.m_bar(t);

                y0[arcIndex + 13 * i + 0] = r[0];
                y0[arcIndex + 13 * i + 1] = r[1];
                y0[arcIndex + 13 * i + 2] = r[2];
                y0[arcIndex + 13 * i + 3] = v[0];
                y0[arcIndex + 13 * i + 4] = v[1];
                y0[arcIndex + 13 * i + 5] = v[2];
                y0[arcIndex + 13 * i + 6] = pv[0];
                y0[arcIndex + 13 * i + 7] = pv[1];
                y0[arcIndex + 13 * i + 8] = pv[2];
                y0[arcIndex + 13 * i + 9] = pr[0];
                y0[arcIndex + 13 * i + 10] = pr[1];
                y0[arcIndex + 13 * i + 11] = pr[2];
                y0[arcIndex + 13 * i + 12] = m;
            }
        }

        public override void Optimize(double t0)
        {
            initializing = false;

            try {
                if ( y0 != null )
                {
                    if ( y0.Length > 13*arcs.Count + arcIndex )
                    {
                        /* probably normal staging, so just shrink */
                        double[] y0_old = y0;
                        y0 = new double[13*arcs.Count + arcIndex];
                        Array.Copy(y0_old, 0, y0, 0, 13*arcs.Count + arcIndex);
                    }
                    else if ( y0.Length != 13*arcs.Count + arcIndex )
                    {
                        y0 = null;
                    }
                }

                NormalizeArcs();

                if (y0 == null)
                {
                    initializing = true;
                    Bootstrap();
                }
                else
                {
                    UpdateY0Arc(0);
                }

                for(int i = 0; i < y0.Length; i++)
                    Debug.Log("n y0[" + i + "] = " + y0[i]);

                if ( runOptimizer(arcs) )
                {
                    if (y0[0] < 0)
                    {
                        y0 = null;
                        return;
                    }

                    Solution sol = new Solution(t_scale, v_scale, r_scale, t0);

                    for(int i = 0; i < y0.Length; i++)
                        Debug.Log("y0[" + i + "] = " + y0[i]);

                    multipleIntegrate(y0, sol, arcs, 10);

                    this.solution = sol;

                    Debug.Log("rf = " + sol.r_bar(sol.tmax()) + "(" + sol.r_bar(sol.tmax()).magnitude + ") vf = " + sol.v_bar(sol.tmax()) + "(" + sol.v_bar(sol.tmax()).magnitude + ")");
                    Debug.Log("rf = " + sol.r(sol.tf()) + "(" + sol.r(sol.tf()).magnitude + ") vf = " + sol.v(sol.tf()) + "(" + sol.v(sol.tf()).magnitude + ")");

                } else {
                    y0 = null;
                }
            }
            catch (Exception e)
            {
                Debug.Log(e);
            }
        }
    }
}