// Geometry and sprite GUIDs from the accepted HTML and native RPG11 prefab capture.
public static class QuestMerchantArtwork
{
    public readonly struct Node
    {
        public readonly string name, guid, color;
        public readonly float x, y, width, height, pixelsPerUnit;
        public readonly bool sliced, flip;
        public Node(string n,string g,string c,float px,float py,float w,float h,float p,bool s,bool f)
        { name=n;guid=g;color=c;x=px;y=py;width=w;height=h;pixelsPerUnit=p;sliced=s;flip=f; }
    }
    public static readonly Node[] Merchant = {
        new Node("","3c89693feec0a6141a8cd8137df9edee","FFFFFFFF",0.000000f,0.000000f,608.000000f,776.000000f,2.000000f,true,false),
        new Node("Borders","43c5667b3f7f73748acff65a2293b240","FFFFFFFF",-7.500000f,-7.500000f,623.000000f,791.000000f,2.000000f,true,false),
        new Node("Header/Border","a1ca6ce62775cf94ba6e754c63f4aeb2","FFFFFFFF",-8.871595f,61.000000f,625.743191f,22.000000f,2.000000f,true,false),
        new Node("Header/Background","319122487c9e2254a9722214eb39d38d","FF6363FF",1.182879f,0.000000f,605.634241f,70.000000f,2.000000f,true,false),
        new Node("Header/Background/Overlay","8f74ffe287b6cdb4d83174b371049c6b","FFFFFFFF",1.182879f,0.000000f,605.634241f,70.000000f,2.000000f,true,false),
        new Node("Header/Background/Middle Glow/Left","d28245afe342bc14e860d50cfb15d03c","FF6363FF",156.140006f,-0.999939f,147.859994f,71.999940f,2.000000f,false,false),
        new Node("Header/Background/Middle Glow/Right","d28245afe342bc14e860d50cfb15d03c","FF6363FF",304.000000f,-0.999939f,147.859922f,71.999940f,2.000000f,false,true),
        new Node("Header/Background/Ornaments/Left","15fb55daf7b0bff41955f4410de9e41b","FFFFFFFF",1.182879f,0.000000f,302.817121f,70.000000f,2.000000f,true,false),
        new Node("Header/Background/Ornaments/Right","15fb55daf7b0bff41955f4410de9e41b","FFFFFFFF",304.000000f,0.000000f,302.817121f,70.000000f,2.000000f,true,true),
        new Node("Header/Background/Effect Left","cfe900353e661ec49bd5972f3ece302d","C83939FF",0.000000f,-4.000000f,312.280156f,71.500000f,2.000000f,false,false),
        new Node("Header/Background/Effect Right","cfe900353e661ec49bd5972f3ece302d","C83939FF",295.719844f,-4.000000f,312.280156f,71.500000f,2.000000f,false,true),
        new Node("Header/Button (Close)","079c064058731e944a55aa8f44c2cea0","FFFFFFFF",550.000000f,20.000000f,36.000000f,36.000039f,2.000000f,false,false),
        new Node("Header/Button (Close)/Foreground","ed944e9a275fd10479acb5bad46ded98","FFFFFFFF",555.890909f,25.890909f,24.218182f,24.218221f,2.000000f,false,false),
        new Node("Header/Button (Close)/Icon","8be1be4978ee6c04b8e13b82b7c88d92","FFFFFFFF",562.109091f,32.109091f,11.781818f,11.781818f,2.000000f,false,false),
    };
    public static readonly Node[] Journal = {
        new Node("","3c89693feec0a6141a8cd8137df9edee","FFFFFFFF",0.000000f,0.000000f,917.400067f,724.000000f,1.818182f,true,false),
        new Node("Borders","43c5667b3f7f73748acff65a2293b240","FFFFFFFF",-16.500000f,-16.499933f,950.400067f,756.999933f,1.818182f,true,false),
        new Node("Header/Border","a1ca6ce62775cf94ba6e754c63f4aeb2","FFFFFFFF",-8.250000f,61.000000f,933.900067f,22.000000f,1.818182f,true,false),
        new Node("Header/Background","319122487c9e2254a9722214eb39d38d","FF6363FF",1.100000f,0.000000f,915.200067f,70.000060f,1.818182f,true,false),
        new Node("Header/Background/Overlay","8f74ffe287b6cdb4d83174b371049c6b","FFFFFFFF",1.100000f,0.000000f,915.200067f,70.000060f,1.818182f,true,false),
        new Node("Header/Background/Middle Glow/Left","d28245afe342bc14e860d50cfb15d03c","FF6363FF",233.200067f,-0.999939f,225.500000f,72.000000f,1.818182f,false,false),
        new Node("Header/Background/Middle Glow/Right","d28245afe342bc14e860d50cfb15d03c","FF6363FF",458.700067f,-0.999939f,225.500000f,72.000000f,1.818182f,false,true),
        new Node("Header/Background/Ornaments/Left","15fb55daf7b0bff41955f4410de9e41b","FFFFFFFF",1.100000f,0.000000f,457.600067f,70.000060f,1.818182f,true,false),
        new Node("Header/Background/Ornaments/Right","15fb55daf7b0bff41955f4410de9e41b","FFFFFFFF",458.700067f,0.000000f,457.600000f,70.000060f,1.818182f,true,true),
        new Node("Header/Background/Effect Left","cfe900353e661ec49bd5972f3ece302d","C83939FF",0.000000f,-3.999939f,290.400067f,71.499940f,1.818182f,false,false),
        new Node("Header/Background/Effect Right","cfe900353e661ec49bd5972f3ece302d","C83939FF",627.000067f,-3.999939f,290.400000f,71.499940f,1.818182f,false,true),
        new Node("Header/Button (Close)","079c064058731e944a55aa8f44c2cea0","FFFFFFFF",859.400000f,20.000000f,36.000000f,36.000000f,1.818182f,false,false),
        new Node("Header/Button (Close)/Foreground","ed944e9a275fd10479acb5bad46ded98","FFFFFFFF",865.290909f,25.890909f,24.218262f,24.218182f,1.818182f,false,false),
        new Node("Header/Button (Close)/Icon","8be1be4978ee6c04b8e13b82b7c88d92","FFFFFFFF",871.509091f,32.109051f,11.781818f,11.781858f,1.818182f,false,false),
    };
}
