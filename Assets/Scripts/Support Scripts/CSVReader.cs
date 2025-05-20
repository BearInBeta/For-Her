using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class CSVReader
{


    public static List<string>[] LoadCSV(string filePath)
    {
        List<string>[] dataArray = null;
        string fullPath = Path.Combine(Application.streamingAssetsPath, filePath);

        try
        {
            string[] lines = File.ReadAllLines(fullPath);
            dataArray = new List<string>[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                string[] row = lines[i].Split(','); // Split by comma
                List<string> rowData = new List<string>();

                foreach (string cell in row)
                {
                    rowData.Add(cell.Trim()); // Trim whitespace and add to list
                }

                dataArray[i] = rowData;
            }

        }
        catch (Exception e)
        {
            Debug.LogError("Error reading CSV file: " + e.Message);
        }

        return dataArray;
    }

    public static List<string> LoadCSVOneColumn(string filePath)
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, filePath);
        List<string> firstColumn = new List<string>();

        try
        {
            string[] lines = File.ReadAllLines(fullPath);

            foreach (string line in lines)
            {
                string[] row = line.Split(',');
                if (row.Length > 0)
                {
                    firstColumn.Add(row[0].Trim());
                }
            }

        }
        catch (Exception e)
        {
            Debug.LogError("Error reading CSV file: " + e.Message);
        }

        return firstColumn;
    }


}
