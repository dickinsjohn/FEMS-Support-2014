using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEMS_Supports
{
    //structure to store specification data from file
    public struct specificationData
    {
        public string selectedFamily;
        public string discipline;
        public double offset;
        public double minSpacing;
        public string supportType;
        public string specsFile;
    };

    //specs sruct for FEMS team
    public struct FEMS_Specs
    {
        public double dia;
        public double spacing;
    };
}
