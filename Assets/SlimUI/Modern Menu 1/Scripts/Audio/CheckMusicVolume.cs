using UnityEngine;
using System.Collections;

namespace SlimUI.ModernMenu{
	public class CheckMusicVolume : MonoBehaviour {
		public void  Start (){
			if (!PlayerPrefs.HasKey("MusicVolume"))
			{
				PlayerPrefs.SetFloat("MusicVolume", 0.75f);
			}
			// remember volume level from last time
			GetComponent<AudioSource>().volume = PlayerPrefs.GetFloat("MusicVolume");
		}

		public void UpdateVolume (){
			GetComponent<AudioSource>().volume = PlayerPrefs.GetFloat("MusicVolume");
		}
	}
}