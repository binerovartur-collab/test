using System;
using System.Runtime.InteropServices;

namespace LazerNi.Interop
{
    /// <summary>
    /// P/Invoke wrapper for EzCad SDK (MarkEzd.dll)
    /// Based on JczLmc.cs from SDK samples
    /// </summary>
    public static class EzCadInterop
    {
        #region Error Codes and Messages

        public static string GetErrorText(int errorCode)
        {
            return errorCode switch
            {
                0 => "Success",
                1 => "Now have a working EZCAD",
                2 => "No found EZCAD.CFG",
                3 => "Open LMC failed",
                4 => "No LMC Board",
                5 => "LMC version Error",
                6 => "No found MarkCfg in Plug",
                7 => "Error Signal",
                8 => "User Stop",
                9 => "Unknown error",
                10 => "Timeout",
                11 => "No Initialization",
                12 => "Read File Error",
                13 => "Full Windows",
                14 => "No found font",
                15 => "Pen error",
                16 => "Object is not text",
                17 => "Save file fail",
                18 => "Save file fail because same object is no found",
                19 => "Now state can not work as command",
                20 => "Error Parameter",
                _ => $"Unknown error code: {errorCode}"
            };
        }

        #endregion

        #region Initialization and Cleanup

        /// <summary>
        /// Initialize the SDK
        /// </summary>
        /// <param name="pathName">Path to MarkEzd.dll directory (where EzCad2.exe is located)</param>
        /// <param name="testMode">True for test mode (no actual laser), False for production</param>
        /// <param name="hOwnerWnd">Handle to owner window (can be IntPtr.Zero)</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_Initial2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Initialize(string pathName, bool testMode, IntPtr hOwnerWnd);

        /// <summary>
        /// Close and release SDK resources
        /// </summary>
        /// <returns>0 for success</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_Close", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Close();

        #endregion

        #region File Operations

        /// <summary>
        /// Load .ezd file template into the current database
        /// </summary>
        /// <param name="fileName">Full path to .ezd file</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_LoadEzdFile", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int LoadEzdFile(string fileName);

        #endregion

        #region Coordinate Transformation

        /// <summary>
        /// Set rotation and move parameters for all objects in the database
        /// CRITICAL: This is the KEY function for camera-laser integration
        /// NOTE: This function returns VOID (not int!) - SDK documentation page 3-4
        /// WARNING: This rotates THE ENTIRE WORK AREA around center, not individual objects!
        /// For rotating individual codes/entities, use RotateEnt instead
        /// </summary>
        /// <param name="moveX">X translation offset in millimeters</param>
        /// <param name="moveY">Y translation offset in millimeters</param>
        /// <param name="centerX">X coordinate of rotation center in millimeters</param>
        /// <param name="centerY">Y coordinate of rotation center in millimeters</param>
        /// <param name="rotateAngle">Rotation angle in RADIANS (not degrees!)</param>
        [DllImport("MarkEzd", EntryPoint = "lmc1_SetRotateMoveParam", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern void SetRotateMoveParam(
            double moveX,
            double moveY,
            double centerX,
            double centerY,
            double rotateAngle);

        /// <summary>
        /// Rotate a specific entity/object in the database
        /// CRITICAL: This rotates THE ENTITY ITSELF (e.g. DataMatrix code), not the entire work area
        /// Use this when you want to rotate the marking code to match product orientation
        /// SDK documentation page 9
        /// </summary>
        /// <param name="pEntName">Name of the entity to rotate (e.g. "DM", "Code1")</param>
        /// <param name="dCenx">X coordinate of rotation center in millimeters</param>
        /// <param name="dCeny">Y coordinate of rotation center in millimeters</param>
        /// <param name="dAngle">Rotation angle in RADIANS (not degrees!)</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_RotateEnt", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int RotateEnt(string pEntName, double dCenx, double dCeny, double dAngle);

        /// <summary>
        /// Move (translate) a specific entity/object in the database
        /// This moves the entity by the specified offset WITHOUT affecting rotation
        /// Use this to apply X,Y positioning after rotating the entity
        /// </summary>
        /// <param name="strEntName">Name of the entity to move (e.g. "DM", "Code1")</param>
        /// <param name="dMovex">X movement offset in millimeters</param>
        /// <param name="dMovey">Y movement offset in millimeters</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MoveEnt", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int MoveEnt(string strEntName, double dMovex, double dMovey);

        #endregion

        #region Marking Operations

        /// <summary>
        /// Mark (engrave) all objects in the current database
        /// </summary>
        /// <param name="flyMode">True for fly marking, False for normal marking</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_Mark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int Mark(bool flyMode);

        /// <summary>
        /// Red light preview - shows marking position with red laser pointer (no actual marking)
        /// SAFE for testing positioning without engraving
        /// </summary>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_RedLightMark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int RedLightMark();

        /// <summary>
        /// Red light preview with contour
        /// </summary>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_RedLightMarkContour", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int RedLightMarkContour();

        /// <summary>
        /// Force stop current marking operation
        /// </summary>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_StopMark", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int StopMark();

        /// <summary>
        /// Check if marking is currently in progress
        /// </summary>
        /// <returns>True if marking, False otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_IsMarking", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern bool IsMarking();

        /// <summary>
        /// Mark on fly mode - waits for hardware trigger signal (IN8/IN9) before marking
        /// IMPORTANT: This function waits for external hardware signal!
        /// Configure trigger input in EzCad "on the fly" parameters window
        /// </summary>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MarkFlyByStartSignal", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int MarkFlyByStartSignal();

        #endregion

        #region Pen Parameters

        /// <summary>
        /// Get pen (laser) parameters for specified pen number
        /// Contains power, speed, frequency and other critical marking settings
        /// </summary>
        /// <param name="nPenNo">Pen number (0-255)</param>
        /// <param name="nMarkLoop">Number of marking loops</param>
        /// <param name="dMarkSpeed">Marking speed in mm/s</param>
        /// <param name="dPowerRatio">Power ratio 0-100%</param>
        /// <param name="dCurrent">Current in A</param>
        /// <param name="nFreq">Frequency in Hz</param>
        /// <param name="dQPulseWidth">Q pulse width in us</param>
        /// <param name="nStartTC">Start delay in us</param>
        /// <param name="nLaserOffTC">Laser off delay in us</param>
        /// <param name="nEndTC">End delay in us</param>
        /// <param name="nPolyTC">Polygon delay in us</param>
        /// <param name="dJumpSpeed">Jump speed in mm/s</param>
        /// <param name="nJumpPosTC">Jump position delay in us</param>
        /// <param name="nJumpDistTC">Jump distance delay in us</param>
        /// <param name="dEndComp">End compensation in mm</param>
        /// <param name="dAccDist">Acceleration distance in mm</param>
        /// <param name="dPointTime">Point time in ms</param>
        /// <param name="bPulsePointMode">Pulse point mode enabled</param>
        /// <param name="nPulseNum">Number of pulses</param>
        /// <param name="dFlySpeed">Fly speed in mm/s</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetPenParam", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetPenParam(
            int nPenNo,
            ref int nMarkLoop,
            ref double dMarkSpeed,
            ref double dPowerRatio,
            ref double dCurrent,
            ref int nFreq,
            ref double dQPulseWidth,
            ref int nStartTC,
            ref int nLaserOffTC,
            ref int nEndTC,
            ref int nPolyTC,
            ref double dJumpSpeed,
            ref int nJumpPosTC,
            ref int nJumpDistTC,
            ref double dEndComp,
            ref double dAccDist,
            ref double dPointTime,
            ref bool bPulsePointMode,
            ref int nPulseNum,
            ref double dFlySpeed);

        /// <summary>
        /// Get the count of objects in the current database
        /// </summary>
        /// <returns>Number of objects in database (unsigned short)</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetEntityCount", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern ushort GetEntityCount();

        /// <summary>
        /// Mark a specific entity by name
        /// Use this for SPI/JPT lasers that may have issues with Mark()
        /// </summary>
        /// <param name="entName">Name of the entity to mark (e.g. "DM", "11")</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MarkEntity", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int MarkEntity(string entName);

        /// <summary>
        /// Mark a specific entity in fly mode
        /// </summary>
        /// <param name="entName">Name of the entity to mark</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_MarkEntityFly", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int MarkEntityFly(string entName);

        /// <summary>
        /// Get the name of entity by index
        /// CORRECT signature: uses StringBuilder, not IntPtr!
        /// </summary>
        /// <param name="nEntityIndex">Index of entity (0-based)</param>
        /// <param name="entName">StringBuilder to receive entity name (initialize with capacity 255)</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetEntityName", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetEntityName(int nEntityIndex, System.Text.StringBuilder entName);

        /// <summary>
        /// Get the size and bounding box of an entity
        /// Used to calculate the center point for rotation
        /// SDK documentation page 9
        /// </summary>
        /// <param name="strEntName">Name of the entity</param>
        /// <param name="dMinx">Minimum X coordinate (output)</param>
        /// <param name="dMiny">Minimum Y coordinate (output)</param>
        /// <param name="dMaxx">Maximum X coordinate (output)</param>
        /// <param name="dMaxy">Maximum Y coordinate (output)</param>
        /// <param name="dZ">Z coordinate (output)</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetEntSize", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetEntSize(string strEntName, ref double dMinx, ref double dMiny, ref double dMaxx, ref double dMaxy, ref double dZ);

        #endregion

        #region Text Operations

        /// <summary>
        /// Change text content of a text entity (DataMatrix, QR, Text, Barcode)
        /// SDK documentation page 15
        /// </summary>
        /// <param name="strTextName">Name of the text entity in template (e.g. "CodeObject", "DM1")</param>
        /// <param name="strTextNew">New text content to set</param>
        /// <returns>0 for success, error code otherwise (16 = object is not text)</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_ChangeTextByName", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int ChangeTextByName(string strTextName, string strTextNew);

        /// <summary>
        /// Get current text content of a text entity
        /// </summary>
        /// <param name="strTextName">Name of the text entity</param>
        /// <param name="strText">StringBuilder to receive text (initialize with capacity 1024)</param>
        /// <returns>0 for success, error code otherwise</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetTextByName", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetTextByName(string strTextName, System.Text.StringBuilder strText);

        #endregion

        #region Preview and UI Functions

        /// <summary>
        /// Get preview bitmap of current template
        /// Returns HBITMAP handle that must be freed with DeleteObject
        /// </summary>
        /// <param name="bmpWidth">Width of preview image</param>
        /// <param name="bmpHeight">Height of preview image</param>
        /// <returns>HBITMAP handle</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_GetPrevBitmap2", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr GetPrevBitmap2(int bmpWidth, int bmpHeight);

        /// <summary>
        /// Delete GDI object (used to free HBITMAP from GetPrevBitmap2)
        /// </summary>
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Open device configuration dialog
        /// </summary>
        /// <returns>0 for success</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_SetDevCfg", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDevCfg();

        /// <summary>
        /// Open EzCad parameters dialog
        /// </summary>
        /// <returns>0 for success</returns>
        [DllImport("MarkEzd", EntryPoint = "lmc1_SetEzCadPara", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int SetEzCadPara();

        #endregion
    }
}
