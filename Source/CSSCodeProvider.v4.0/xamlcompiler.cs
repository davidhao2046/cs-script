using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.CodeDom.Compiler;
using Microsoft.JScript;
using System.CodeDom;
using Microsoft.CSharp;


namespace CSScriptCompilers //CS-Script
{
    /// <summary>
    /// C#+XAML compiler. 
    /// This class is capable of compiling (with MSBuild) dynamically created VS C# WPF project based on cs file(s) 
    /// </summary>
    public class CSCompiler : ICodeCompiler
    {
        string version;
        public CSCompiler(string version)
        {
            this.version = version;
        }
        #region Dummy interface implementations
        public CompilerResults CompileAssemblyFromDom(CompilerParameters options, CodeCompileUnit compilationUnit)
        {
            throw new NotImplementedException("CompileAssemblyFromDom is not implemented");
        }
        public CompilerResults CompileAssemblyFromDomBatch(CompilerParameters options, CodeCompileUnit[] compilationUnits)
        {
            throw new NotImplementedException("CompileAssemblyFromDomBatch is not implemented");
        }
        public CompilerResults CompileAssemblyFromFile(CompilerParameters options, string fileName)
        {
            throw new NotImplementedException("CompileAssemblyFromFile is not implemented");
        }
        public CompilerResults CompileAssemblyFromSource(CompilerParameters options, string source)
        {
            throw new NotImplementedException("CompileAssemblyFromSource is not implemented");
        }
        public CompilerResults CompileAssemblyFromSourceBatch(CompilerParameters options, string[] sources)
        {
            throw new NotImplementedException("CompileAssemblyFromSourceBatch is not implemented");
        }
        #endregion

        const string reference_template =
                                    "<Reference Include=\"{0}\">\n" +
                                    "  <SpecificVersion>False</SpecificVersion>\n" +
                                    "  <HintPath>{1}</HintPath>" +
                                    "</Reference>";
        const string include_template =
                                    "<Compile Include=\"{0}\">\n" +
                                    "  <!-- <DependentUpon>.xaml</DependentUpon> -->\n" +
                                    "  <SubType>Code</SubType>\n" +
                                    "</Compile>";
        public CompilerResults CompileAssemblyFromFileBatch(CompilerParameters options, string[] fileNames)
        {
            //System.Diagnostics.Debug.Assert(false);
            foreach (string file in fileNames)
                if (file.ToLower().EndsWith(".xaml"))
                    return CompileAssemblyFromFileBatchImpl(options, fileNames);

            if (version == "" || version == null)
                return new CSharpCodeProvider().CreateCompiler().CompileAssemblyFromFileBatch(options, fileNames);
            else
                return new CSharpCodeProvider(new Dictionary<string, string>() { { "CompilerVersion", version } }).CreateCompiler().CompileAssemblyFromFileBatch(options, fileNames);
        }
        //static bool appIncluded = false;
        static bool IsAppXAML(string file)
        {
            return false; //do not support app.xaml
            //if (!appIncluded)
            //{
            //    using (StreamReader sr = new StreamReader(file))
            //    {
            //        if (sr.ReadToEnd().IndexOf("<Application x:Class") == -1)
            //            return false;

            //        appIncluded = true;
            //        return true;
            //    }
            //}
            //else
            //    return false;
        }
        CompilerResults CompileAssemblyFromFileBatchImpl(CompilerParameters options, string[] fileNames)
        {
            //System.Diagnostics.Debug.Assert(false);
            CompilerResults retval = new CompilerResults(new TempFileCollection());

            string outputName = Path.GetFileNameWithoutExtension(options.OutputAssembly);
            string tempDir = Path.Combine(Path.GetTempPath(), "CSSCRIPT\\CPP\\" + System.Guid.NewGuid().ToString());
            string tempProj = Path.Combine(tempDir, outputName + ".csproj");
            string tempSln = Path.Combine(tempDir, outputName + ".sln");
            string outputFile = Path.Combine(tempDir, outputName + (options.GenerateExecutable ? ".exe" : ".dll"));

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);

            Directory.CreateDirectory(tempDir);

            File.WriteAllText(tempSln, GetSolutionTemplateContent().Replace("$PROJECT_FILE$", outputName + ".csproj"));

            string content = GetProjTemplateContent();

            using (StreamWriter sw = new StreamWriter(tempProj))
            {
                content = content.Replace("$NAME$", outputName);
                content = content.Replace("$DEBUG_TYPE$", options.IncludeDebugInformation ? "<DebugType>full</DebugType>" : "");
                content = content.Replace("$OPTIMIZE$", options.IncludeDebugInformation ? "false" : "true");
                content = content.Replace("$DEBUG_CONST$", options.IncludeDebugInformation ? "DEBUG;" : "");
                content = content.Replace("$DEBUG$", options.IncludeDebugInformation ? "true" : "false");
                content = content.Replace("$OUPTUT_DIR$", tempDir);

                //Exe/WinExe/Library
                if (options.GenerateExecutable) //exe
                {
                    if (options.CompilerOptions != null && options.CompilerOptions.IndexOf("/target:winexe") != -1)
                    {
                        content = content.Replace("$TYPE$", "WinExe");	//WinForm
                    }
                    else
                    {
                        content = content.Replace("$TYPE$", "Exe");	//console
                    }
                }
                else //dll
                {
                    content = content.Replace("$TYPE$", "Library");	//dll
                }

                string references = "";
                foreach (string file in options.ReferencedAssemblies)
                    references += string.Format(reference_template, Path.GetFileName(file), file);
                content = content.Replace("$REFERENCES$", references);

                content = content.Replace("$MIN_CLR_VER$", "<MinFrameworkVersionRequired>4.0</MinFrameworkVersionRequired>");
                //content = content.Replace("$IMPORT_PROJECT$", "<Import Project=\"$(MSBuildBinPath)\\Microsoft.WinFX.targets\" />");
                content = content.Replace("$IMPORT_PROJECT$", "");
                string sources = "";

                foreach (string file in fileNames)
                {
                    if (file.ToLower().EndsWith(".xaml"))
                    {
                        if (IsAppXAML(file))
                            sources += "<ApplicationDefinition Include=\"" + file + "\" />\n";
                        else
                            sources += "<Page Include=\"" + file + "\" />\n";
                    }
                    else
                        sources += string.Format(include_template, file);
                }
                content = content.Replace("$SOURCE_FILES$", sources);

                sw.Write(content);
            }
             
            string compileLog = "";
            //Stopwatch sw1 = new Stopwatch();
            //sw1.Start();

            string msbuild = Path.Combine(Path.GetDirectoryName("".GetType().Assembly.Location), "MSBuild.exe");

            string args = string.Format(@"/nologo /verbosity:minimal /t:Clean,Build /p:Configuration=CSSBuild /p:Platform=""Any CPU"" ""{0}""", tempSln);

            compileLog = RunApp(Path.GetDirectoryName(tempProj), msbuild, args).Trim();
            //compileLog = RunApp(Path.GetDirectoryName(tempProj), msbuild, "\"" + tempProj + "\" /p:Configuration=\"CSSBuild\" /nologo /verbosity:m").Trim();

            //sw1.Stop();

            if (compileLog.EndsWith("-- FAILED."))
            {
                using (StringReader sr = new StringReader(compileLog))
                {
                    string line = "";
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();

                        if (line == "")
                            continue;

                        if (line.EndsWith("Done building project"))
                            break;

                        int lineNumber = 0;
                        int colNumber = 0;
                        string fileName = "";
                        string errorNumber = "";
                        string errorText = "";
                        bool isWarning = false;

                        int fileEnd = line.IndexOf(": warning ");
                        if (fileEnd == -1)
                            fileEnd = line.IndexOf(": error ");
                        else
                            isWarning = true;

                        if (fileEnd == -1)
                            continue;

                        string filePart = line.Substring(0, fileEnd);
                        string errorPart = line.Substring(fileEnd + 2); //" :" == 2
                        int lineNumberStart = filePart.LastIndexOf("(");
                        int errorDescrStart = errorPart.IndexOf(":");

                        string[] erorLocation = filePart.Substring(lineNumberStart).Replace("(", "").Replace(")", "").Split(',');
                        lineNumber = filePart.EndsWith(")") ? int.Parse(erorLocation[0]) : -1;
                        colNumber = filePart.EndsWith(")") ? int.Parse(erorLocation[1]) : -1;
                        fileName = Path.GetFullPath(lineNumber == -1 ? filePart : filePart.Substring(0, lineNumberStart).Trim());
                        errorNumber = errorPart.Substring(0, errorDescrStart).Trim();
                        errorText = errorPart.Substring(errorDescrStart + 1).Trim();

                        CompilerError error = new CompilerError(fileName, lineNumber, colNumber, errorNumber, errorText);
                        error.IsWarning = isWarning;

                        retval.Errors.Add(error);
                    }
                }
            }

            if (File.Exists(outputFile))
            {
                if (File.Exists(options.OutputAssembly))
                    File.Copy(outputFile, options.OutputAssembly, true);
                else
                    File.Move(outputFile, options.OutputAssembly);
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch { }
            }

            if (options.IncludeDebugInformation)
            {
                string pdbSrcFile = Path.ChangeExtension(outputFile, ".pdb");
                string pdbDestFile = Path.ChangeExtension(options.OutputAssembly, ".pdb");
                if (File.Exists(pdbSrcFile))
                {
                    if (File.Exists(pdbDestFile))
                        File.Copy(pdbSrcFile, pdbDestFile, true);
                    else
                        File.Move(pdbSrcFile, pdbDestFile);
                }
            }
            return retval;
        }

        static string GetSolutionTemplateContent()
        {
            return @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 14
VisualStudioVersion = 14.0.25123.0
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""onfly.xaml.cs"", ""$PROJECT_FILE$"", ""{31BEEBF9-835A-4A03-BBB6-EFC6A9CB293F}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		CSSBuild|Any CPU = CSSBuild|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{31BEEBF9-835A-4A03-BBB6-EFC6A9CB293F}.CSSBuild|Any CPU.ActiveCfg = CSSBuild|Any CPU
		{31BEEBF9-835A-4A03-BBB6-EFC6A9CB293F}.CSSBuild|Any CPU.Build.0 = CSSBuild|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal";
        }
        static string GetProjTemplateContent()
        {
            var file = ProjTemplateFile;
            if (File.Exists(file))
            {
                using (StreamReader sr = new StreamReader(file))
                    return sr.ReadToEnd();
            }
            else
                return @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"" ToolsVersion=""4.0"">
  <PropertyGroup>
    <Configuration Condition="" '$(Configuration)' == '' "">CSSBuild</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>
    <ProjectGuid>{31BEEBF9-835A-4A03-BBB6-EFC6A9CB293F}</ProjectGuid>
    <ProjectTypeGuids>{60dc8134-eba5-43b8-bcc9-bb4bc16c2548};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
    <AssemblyName>$NAME$</AssemblyName>
    <WarningLevel>4</WarningLevel>
    <OutputType>$TYPE$</OutputType>
    $MIN_CLR_VER$
	<TargetFrameworkVersion>v4.0</TargetFrameworkVersion>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'CSSBuild|AnyCPU' "">
    <DebugSymbols>$DEBUG$</DebugSymbols>
	$DEBUG_TYPE$
    <Optimize>$OPTIMIZE$</Optimize>
    <OutputPath>$OUPTUT_DIR$</OutputPath> 
    <DefineConstants>$DEBUG_CONST$TRACE</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    $REFERENCES$
  </ItemGroup>
  <ItemGroup>
    $SOURCE_FILES$
  </ItemGroup>
  <Import Project=""$(MSBuildBinPath)\Microsoft.CSharp.targets"" />
  $IMPORT_PROJECT$
</Project>";
        }

        static string ProjTemplateFile
        {
            get
            {
                string file;
                //return @"C:\cs-script\Dev\WPF\VS\xaml.template";
                if (Environment.GetEnvironmentVariable("CSScriptDebugging") != null || Environment.GetEnvironmentVariable("CSScriptDebugging") == null)
                    file = Environment.ExpandEnvironmentVariables(@"%CSSCRIPT_DIR%\Lib\xaml.template");
                else
                    file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"xaml.template");

                return file;
            }
        }
        static string RunApp(string workingDir, string app, string args)
        {
            Process myProcess = new Process();
            myProcess.StartInfo.FileName = app;
            myProcess.StartInfo.Arguments = args;
            myProcess.StartInfo.WorkingDirectory = workingDir;
            myProcess.StartInfo.UseShellExecute = false;
            myProcess.StartInfo.RedirectStandardOutput = true;
            myProcess.StartInfo.CreateNoWindow = true;
            myProcess.Start();

            StringBuilder builder = new StringBuilder();

            string line = null;
            while (null != (line = myProcess.StandardOutput.ReadLine()))
            {
                builder.Append(line + "\n");
            }
            myProcess.WaitForExit();

            return builder.ToString();
        }

    }

    class XAMLTest
    {
        static void _Main()
        {
            bool dll = false;
            string source1 = Environment.ExpandEnvironmentVariables(@"C:\cs-script\Dev\WPF\vs\Window1.cs");
            string source2 = Environment.ExpandEnvironmentVariables(@"C:\cs-script\Dev\WPF\vs\Window1.xaml");
            string source3 = Environment.ExpandEnvironmentVariables(@"C:\cs-script\Dev\WPF\vs\App.xaml");

            CompilerParameters options = new CompilerParameters(
                new string[]
                {
                    @"C:\WINDOWS\assembly\GAC_MSIL\System\2.0.0.0__b77a5c561934e089\System.dll",
                    @"C:\WINDOWS\assembly\GAC_32\System.Data\2.0.0.0__b77a5c561934e089\System.Data.dll",
                    @"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0\WindowsBase.dll",
                    @"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0\PresentationCore.dll",
                    @"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0\PresentationFramework.dll"
                },
                Path.ChangeExtension(source1, dll ? ".dll" : ".exe"),
                false);

            options.GenerateExecutable = !dll;
            options.CompilerOptions += "/target:winexe ";
            options.IncludeDebugInformation = true;

            CompilerResults result = new CSCompiler("v3.5").CompileAssemblyFromFileBatch(options, new string[]
                {
					//source3, 
					source2, source1
                });
        }
    }
}
