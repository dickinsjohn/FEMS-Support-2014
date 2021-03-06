﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Autodesk.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;

namespace FEMS_Supports
{
    public static class UtilityMethods
    {
        //get the 3D view to work on
        public static View3D Get3D_View(Document m_document)
        {
            View3D view3D = null;
            try
            {
                //generate a 3D view for the reference intersector class to work
                view3D = (from v in new FilteredElementCollector(m_document).OfClass(typeof(View3D)).Cast<View3D>()
                          where v.IsTemplate == false && v.IsPerspective == false
                          select v).First();
            }
            catch
            {
                return null;
            }
            return view3D;
        }

        //get the bounding box for sectionbox of the 3D view
        public static BoundingBoxXYZ GetBounds(View3D view)
        {
            BoundingBoxXYZ bounds = new BoundingBoxXYZ();
            try
            {
                bounds = view.GetSectionBox();
                view.IsSectionBoxActive = false;
            }
            catch
            {
                return null;
            }
            return bounds;
        }

        //set the bounding box for the sectionbox of 3D view
        public static bool SetBounds(BoundingBoxXYZ bounds, View3D view)
        {
            try
            {
                view.IsSectionBoxActive = true;
                view.SetSectionBox(bounds);
            }
            catch
            {
                return false;
            }
            return true;
        }

        //method to extend the rod height to structure above
        public static double ExtendRod(XYZ point, Document m_document, View3D view3D)
        {
            double leastProximity = -1.0;

            try
            {
                //create an instance of the refernce intersector and make it to intersect in Elements and revit links
                ReferenceIntersector intersector = new ReferenceIntersector(view3D);
                intersector.TargetType = FindReferenceTarget.Element;
                intersector.FindReferencesInRevitLinks = true;

                //find the points of intersection
                IList<ReferenceWithContext> referenceWithContext = intersector.Find(point, XYZ.BasisZ);

                //remove the intersection on the pipe inside which the point lies
                for (int i = 0; i < referenceWithContext.Count; i++)
                {
                    if (m_document.GetElement(referenceWithContext[i].GetReference()).GetType().Name != "RevitLinkInstance")
                    {
                        referenceWithContext.RemoveAt(i);
                        i--;
                    }
                }

                //if the referncewithcontext is empty return to the calling method
                if (referenceWithContext.Count == 0)
                {
                    return -1;
                }

                //find the least proximity
                leastProximity = referenceWithContext.First().Proximity;

                for (int i = 0; i < referenceWithContext.Count; i++)
                {
                    if (leastProximity > referenceWithContext[i].Proximity)
                    {
                        leastProximity = referenceWithContext[i].Proximity;
                    }
                }
            }
            catch
            {
                return -1;
            }
            //return least proximity found above
            return leastProximity;
        }


        //method to align created element
        public static bool AdjustElement(Document m_document, Element createdInstance, XYZ instancePoint, Pipe pipeElement, double rodLength, Curve pipeCurve)
        {
            try
            {
                if (pipeCurve is Line)
                {
                    Line pipeLine = (Line)pipeCurve;

                    //axis to find the y angle
                    Line yAngleAxis = Line.CreateBound(pipeLine.GetEndPoint(0), new XYZ(pipeLine.GetEndPoint(1).X, pipeLine.GetEndPoint(1).Y, pipeLine.GetEndPoint(0).Z));

                    double yAngle = XYZ.BasisY.AngleTo(yAngleAxis.Direction);

                    //axis of rotation
                    Line axis = Line.CreateBound(instancePoint, new XYZ(instancePoint.X, instancePoint.Y, instancePoint.Z + 10));

                    if (pipeCurve.GetEndPoint(0).Y > pipeCurve.GetEndPoint(1).Y)
                    {
                        if (pipeCurve.GetEndPoint(0).X > pipeCurve.GetEndPoint(1).X)
                        {
                            //rotate the created family instance to align with the pipe
                            ElementTransformUtils.RotateElement(m_document, createdInstance.Id, axis, Math.PI + yAngle);
                        }
                        else if (pipeCurve.GetEndPoint(0).X < pipeCurve.GetEndPoint(1).X)
                        {
                            //rotate the created family instance to align with the pipe
                            ElementTransformUtils.RotateElement(m_document, createdInstance.Id, axis, 2 * Math.PI - yAngle);
                        }
                    }
                    else if (pipeCurve.GetEndPoint(0).Y < pipeCurve.GetEndPoint(1).Y)
                    {
                        if (pipeCurve.GetEndPoint(0).X > pipeCurve.GetEndPoint(1).X)
                        {
                            //rotate the created family instance to align with the pipe
                            ElementTransformUtils.RotateElement(m_document, createdInstance.Id, axis, Math.PI + yAngle);
                        }
                        else if (pipeCurve.GetEndPoint(0).X < pipeCurve.GetEndPoint(1).X)
                        {
                            //rotate the created family instance to align with the pipe
                            ElementTransformUtils.RotateElement(m_document, createdInstance.Id, axis, 2 * Math.PI - yAngle);
                        }
                    }
                    else
                    {
                        //rotate the created family instance to align with the pipe
                        ElementTransformUtils.RotateElement(m_document, createdInstance.Id, axis, yAngle);
                    }

                    ParameterSet parameters = createdInstance.Parameters;

                    //set the Nominal radius and Rod height parameters
                    foreach (Parameter para in parameters)
                    {
                        if (para.Definition.Name == "Nominal Rad")
                        {
                            para.Set(pipeElement.Diameter / 2.0);
                        }
                        if (para.Definition.Name == "Rod Height")
                        {
                            para.Set(rodLength);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            return true;
        }


        //method to get the points for family placement
        public static List<XYZ> GetPlacementPoints(double spacing, Curve pipeCurve, double offset, double minSpacing)
        {
            List<XYZ> points = new List<XYZ>();
            try
            {
                if (spacing == 0)
                {
                    return null;
                }

                double length = 304.8 * pipeCurve.Length;

                //ratios are required for computing the points using eveluate methods 
                //ratios should be within [0,1]
                double startRatio = offset / length;
                double endRatio = (length - offset) / length;

                points.Clear();

                double param = 0;

                //check whether the spacing is less than or greater than length of pipe
                if (length < spacing)
                {
                    //check if spacing minus offsets will be less than or 
                    //greater than minimum spacing between pipes
                    if ((length - 2 * offset) < minSpacing)
                    {
                        points.Add(pipeCurve.Evaluate(0.5, true));
                    }
                    else
                    {
                        points.Add(pipeCurve.Evaluate(startRatio, true));
                        points.Add(pipeCurve.Evaluate(endRatio, true));
                    }
                }
                else
                {
                    //compute number of splits required
                    int splits = (int)length / (int)spacing;

                    //convert splits into fractions so taht it can be compared with [0,1] range
                    param = (double)1 / (splits + 1);

                    //increment param so find required points within the range of [0,1]
                    for (double i = param; i < 1; i += param)
                    {
                        points.Add(pipeCurve.Evaluate(i, true));
                    }

                    points.Add(pipeCurve.Evaluate(startRatio, true));
                    points.Add(pipeCurve.Evaluate(endRatio, true));
                }
            }
            catch
            {
                return null;
            }
            return points;
        }


        //method to get family symbol
        public static FamilySymbol GetFamilySymbol(Family family)
        {
            FamilySymbol symbol = null;
            try
            {
                foreach (FamilySymbol s in family.Symbols)
                {
                    symbol = s;
                    break;
                }
            }
            catch
            {
                return null;
            }
            return symbol;
        }


        //method to Read data from the configuration file
        public static specificationData ReadConfig()
        {
            specificationData fileData = new specificationData();
            fileData.offset = -1.0;

            string configurationFile = null, assemblyLocation = null;
            try
            {
                //open configuration file
                assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;

                //convert the active file path into directory name
                if (File.Exists(assemblyLocation))
                {
                    assemblyLocation = new FileInfo(assemblyLocation).Directory.ToString();
                }

                //get parent directory of the current directory
                if (Directory.Exists(assemblyLocation))
                {
                    assemblyLocation = Directory.GetParent(assemblyLocation).ToString();
                }
            }
            catch
            {
                fileData.offset = -1.0;
                return fileData;
            }

            try
            {
                configurationFile = assemblyLocation + @"\Spacing Configuration\Configuration.txt";

                if (!(File.Exists(configurationFile)))
                {
                    MessageBox.Show("Configuration file doesn't exist!");
                    return fileData;
                }
                else
                {
                    //read all the contents of the file into a string array
                    string[] fileContents = File.ReadAllLines(configurationFile);

                    for (int i = 0; i < fileContents.Count(); i++)
                    {
                        if (fileContents[i].Contains("SelectedFamily: "))
                        {
                            fileData.selectedFamily = fileContents[i].Replace("SelectedFamily: ", "");
                        }
                        else if (fileContents[i].Contains("Discipline: "))
                        {
                            fileData.discipline = fileContents[i].Replace("Discipline: ", "");
                        }
                        else if (fileContents[i].Contains("Offest: "))
                        {
                            fileData.offset = double.Parse(fileContents[i].Replace("Offest: ", "").Trim());
                        }
                        else if (fileContents[i].Contains("Spacing: "))
                        {
                            fileData.minSpacing = double.Parse(fileContents[i].Replace("Spacing: ", "").Trim());
                        }
                        else if (fileContents[i].Contains("SupportType: "))
                        {
                            fileData.supportType = fileContents[i].Replace("SupportType: ", "");
                        }
                        else if (fileContents[i].Contains("File Location: "))
                        {
                            fileData.specsFile = fileContents[i].Replace("File Location: ", "");
                        }
                    }
                }
            }
            catch
            {
                fileData.offset = -1.0;
                return fileData;
            }
            return fileData;
        }


        //method to find the element by family name
        public static Element FindElementByName(Document m_document, Type targetType, string targetName)
        {
            try
            {
                return new FilteredElementCollector(m_document).OfClass(targetType)
                    .FirstOrDefault<Element>(e => e.Name.Equals(targetName));
            }
            catch
            {
                return null;
            }
        }


        //get specificaions regarding the dia and spacing
        public static List<FEMS_Specs> GetFEMS_Specs(specificationData configFileData)
        {
            List<FEMS_Specs> specList = new List<FEMS_Specs>();
            FEMS_Specs specs = new FEMS_Specs();

            try
            {

                if (File.Exists(configFileData.specsFile))
                {
                    //read all the contents of the file into a string array
                    string[] fileContents = File.ReadAllLines(configFileData.specsFile);

                    string[] splitLines = null;

                    for (int i = 0; i < fileContents.Count(); i++)
                    {
                        splitLines = fileContents[i].Split(' ');

                        if (splitLines[0] != "" && splitLines[1] != "")
                        {
                            specs.dia = double.Parse(splitLines[0].Trim());
                            specs.spacing = double.Parse(splitLines[1].Trim());
                            specList.Add(specs);
                        }
                    }

                }
            }
            catch
            {
                return null;
            }
            return specList;
        }


        //method to make choice for placing family instances based on the pipe diameter
        public static double GetSpacing(Element ele, List<FEMS_Specs> specList)
        {
            double dia = 0;
            Curve pipeCurve = null;
            FEMS_Specs diaAndSpacing = new FEMS_Specs();

            try
            {
                diaAndSpacing.dia = 0.0;
                diaAndSpacing.spacing = 0.0;

                List<XYZ> points = new List<XYZ>();

                if (Math.Round(((LocationCurve)ele.Location).Curve.GetEndPoint(0).Z, 5)
                    == Math.Round(((LocationCurve)ele.Location).Curve.GetEndPoint(1).Z, 5))
                {
                    pipeCurve = ((LocationCurve)ele.Location).Curve;

                    //converting dia dn length into millimeters
                    dia = 304.8 * ele.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();

                    foreach (FEMS_Specs specs in specList)
                    {
                        if (specs.dia == dia)
                        {
                            diaAndSpacing = specs;
                            break;
                        }
                    }
                }
            }
            catch
            {
                return -1;
            }
            return diaAndSpacing.spacing;
        }
    }
}
