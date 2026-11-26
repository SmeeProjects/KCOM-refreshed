/*
    Kiwi's Co-Op Mod for Half-Life: Alyx
    Copyright (c) 2022 KiwifruitDev
    All rights reserved.
    This software is licensed under the MIT License.
    -----------------------------------------------------------------------------
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
    -----------------------------------------------------------------------------
*/
using System.Globalization;

namespace KiwisCoOpModCore
{
    // Numbers sent to or read from the game always use "." for decimals, whatever the PC's culture:
    // the game's Lua tonumber("12,5") returns nil (UPGRADE_PLAN.md B1).
    public static class GameNumber
    {
        public static string Format(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        public static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        // NumberStyles.Float, not the default Float | AllowThousands: that reads "12,5" as 125.
        public static float ParseFloat(string value)
        {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        public static int ParseInt(string value)
        {
            return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
