using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ItemState : MonoBehaviour
{
    public bool Enabled = false;
    public Sprite EnabledSprite;
    public Sprite DisabledSprite;
    public ParticleSystem Part;

    public Image ItemImage;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void StartParticle()
    {
        Part.Play();
    }
    public void EnableItem()
    {
        Enabled = true;
        ItemImage.sprite = EnabledSprite;
    }
    public void DisableItem()
    {
        Enabled = false;
        ItemImage.sprite = DisabledSprite;
    }
}
