using System.Collections;

namespace ZedGraph
{
    public class MultiDimBitArray
    {
        private BitArray bitArray;
        private int width;
        private int height;
        public MultiDimBitArray( int width, int height)
        {
            this.width = width+1;
            this.height = height+1;
            bitArray = new BitArray(this.width * this.height);
        }

        public bool this[ int index1, int index2]
        {
            get { return bitArray[index2*width + index1]; }
            set { bitArray[index2*width + index1] = value; }
        }

        public void Clear()
        {
            bitArray.SetAll(false);
        }

        public void TryResize( int width, int height)
        {
            this.width = width+1;
            this.height = height+1;
            if( this.width * this.height > bitArray.Count)
            {
                bitArray = new BitArray((width+1) * (height+1));
            }
            Clear();
        }
    }
}